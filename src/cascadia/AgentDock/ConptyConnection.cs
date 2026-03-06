using Microsoft.Terminal.Wpf;
using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace AgentDock;

internal sealed class ConptyConnection : ITerminalConnection, IDisposable
{
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint ProcThreadAttributePseudoConsole = 0x00020016;
    private const uint Infinite = 0xFFFFFFFF;

    private readonly string _commandLine;
    private readonly object _sync = new();
    private readonly CancellationTokenSource _shutdown = new();

    private bool _started;
    private bool _disposed;
    private IntPtr _pseudoConsole;
    private IntPtr _processHandle;
    private IntPtr _threadHandle;
    private SafeFileHandle _inputReadSide;
    private SafeFileHandle _outputWriteSide;
    private StreamWriter _inputWriter;
    private FileStream _outputStream;

    public ConptyConnection(string commandLine)
    {
        _commandLine = commandLine;
    }

    public event EventHandler<TerminalOutputEventArgs> TerminalOutput;

    public void Start()
    {
        lock (_sync)
        {
            if (_started || _disposed)
            {
                return;
            }

            _started = true;
        }

        SafeFileHandle inputReadSide = null;
        SafeFileHandle inputWriteSide = null;
        SafeFileHandle outputReadSide = null;
        SafeFileHandle outputWriteSide = null;
        StreamWriter inputWriter = null;
        FileStream outputStream = null;
        IntPtr pseudoConsole = IntPtr.Zero;
        IntPtr attributeList = IntPtr.Zero;
        PROCESS_INFORMATION processInfo = default;

        try
        {
            CreatePipe(out inputReadSide, out inputWriteSide);
            CreatePipe(out outputReadSide, out outputWriteSide);

            ThrowIfFailed(
                NativeMethods.CreatePseudoConsole(CreateSize(30, 120), inputReadSide, outputWriteSide, 0, out pseudoConsole),
                "Could not create pseudo console.");

            STARTUPINFOEX startupInfo = ConfigureStartupInfo(pseudoConsole, out attributeList);
            processInfo = StartProcess(ref startupInfo, _commandLine);

            NativeMethods.DeleteProcThreadAttributeList(attributeList);
            Marshal.FreeHGlobal(attributeList);
            attributeList = IntPtr.Zero;

            inputWriter = new StreamWriter(new FileStream(inputWriteSide, FileAccess.Write), new UTF8Encoding(false))
            {
                AutoFlush = true,
            };
            inputWriteSide = null;

            outputStream = new FileStream(outputReadSide, FileAccess.Read);
            outputReadSide = null;

            _pseudoConsole = pseudoConsole;
            _processHandle = processInfo.hProcess;
            _threadHandle = processInfo.hThread;
            _inputReadSide = inputReadSide;
            _outputWriteSide = outputWriteSide;
            _inputWriter = inputWriter;
            _outputStream = outputStream;

            _ = Task.Run(() => PumpOutputAsync(_shutdown.Token));
            _ = Task.Run(() => WaitForProcessExit(_shutdown.Token));

            pseudoConsole = IntPtr.Zero;
            inputReadSide = null;
            outputWriteSide = null;
            inputWriter = null;
            outputStream = null;
            processInfo = default;
        }
        catch
        {
            lock (_sync)
            {
                _started = false;
            }

            if (attributeList != IntPtr.Zero)
            {
                NativeMethods.DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }

            inputWriter?.Dispose();
            outputStream?.Dispose();
            inputReadSide?.Dispose();
            inputWriteSide?.Dispose();
            outputReadSide?.Dispose();
            outputWriteSide?.Dispose();

            if (processInfo.hThread != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(processInfo.hThread);
            }

            if (processInfo.hProcess != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(processInfo.hProcess);
            }

            if (pseudoConsole != IntPtr.Zero)
            {
                NativeMethods.ClosePseudoConsole(pseudoConsole);
            }

            throw;
        }
    }

    public void WriteInput(string data)
    {
        if (_disposed || string.IsNullOrEmpty(data))
        {
            return;
        }

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _inputWriter?.Write(data);
        }
    }

    public void Resize(uint rows, uint columns)
    {
        if (_disposed || _pseudoConsole == IntPtr.Zero || rows == 0 || columns == 0)
        {
            return;
        }

        NativeMethods.ResizePseudoConsole(_pseudoConsole, CreateSize(rows, columns));
    }

    public void Close()
    {
        Dispose();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _shutdown.Cancel();

        _inputWriter?.Dispose();
        _inputWriter = null;

        _outputStream?.Dispose();
        _outputStream = null;

        if (_pseudoConsole != IntPtr.Zero)
        {
            NativeMethods.ClosePseudoConsole(_pseudoConsole);
            _pseudoConsole = IntPtr.Zero;
        }

        _inputReadSide?.Dispose();
        _inputReadSide = null;

        _outputWriteSide?.Dispose();
        _outputWriteSide = null;

        if (_threadHandle != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_threadHandle);
            _threadHandle = IntPtr.Zero;
        }

        if (_processHandle != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_processHandle);
            _processHandle = IntPtr.Zero;
        }

        _shutdown.Dispose();
    }

    private async Task PumpOutputAsync(CancellationToken cancellationToken)
    {
        char[] buffer = new char[4096];

        try
        {
            using StreamReader reader = new(_outputStream, new UTF8Encoding(false), false, 4096, true);

            while (!cancellationToken.IsCancellationRequested)
            {
                int count = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                if (count == 0)
                {
                    break;
                }

                PostOutput(new string(buffer, 0, count));
            }
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            RequestClose();
        }
    }

    private void WaitForProcessExit(CancellationToken cancellationToken)
    {
        try
        {
            if (_processHandle != IntPtr.Zero)
            {
                NativeMethods.WaitForSingleObject(_processHandle, Infinite);
            }
        }
        catch
        {
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            RequestClose();
        }
    }

    private void PostOutput(string data)
    {
        EventHandler<TerminalOutputEventArgs> handler = TerminalOutput;
        if (handler == null || string.IsNullOrEmpty(data))
        {
            return;
        }

        DispatcherOperation(() => handler(this, new TerminalOutputEventArgs(data)));
    }

    private void RequestClose()
    {
        DispatcherOperation(Close);
    }

    private static STARTUPINFOEX ConfigureStartupInfo(IntPtr pseudoConsole, out IntPtr attributeList)
    {
        IntPtr attributeListSize = IntPtr.Zero;
        bool initialized = NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeListSize);

        if (initialized || attributeListSize == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not calculate the attribute list size.");
        }

        attributeList = Marshal.AllocHGlobal(attributeListSize);

        STARTUPINFOEX startupInfo = new();
        startupInfo.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
        startupInfo.lpAttributeList = attributeList;

        if (!NativeMethods.InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeListSize))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not initialize the attribute list.");
        }

        if (!NativeMethods.UpdateProcThreadAttribute(
                attributeList,
                0,
                (IntPtr)ProcThreadAttributePseudoConsole,
                pseudoConsole,
                (IntPtr)IntPtr.Size,
                IntPtr.Zero,
                IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not attach the pseudo console to the process.");
        }

        return startupInfo;
    }

    private static PROCESS_INFORMATION StartProcess(ref STARTUPINFOEX startupInfo, string commandLine)
    {
        SECURITY_ATTRIBUTES processAttributes = new()
        {
            nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
        };

        SECURITY_ATTRIBUTES threadAttributes = new()
        {
            nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
        };

        if (!NativeMethods.CreateProcess(
                null,
                commandLine,
                ref processAttributes,
                ref threadAttributes,
                false,
                ExtendedStartupInfoPresent,
                IntPtr.Zero,
                null,
                ref startupInfo,
                out PROCESS_INFORMATION processInfo))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not launch the child process.");
        }

        return processInfo;
    }

    private static COORD CreateSize(uint rows, uint columns)
    {
        return new COORD
        {
            X = (short)columns,
            Y = (short)rows,
        };
    }

    private static void CreatePipe(out SafeFileHandle readSide, out SafeFileHandle writeSide)
    {
        if (!NativeMethods.CreatePipe(out readSide, out writeSide, IntPtr.Zero, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create a pseudo console pipe.");
        }
    }

    private static void ThrowIfFailed(int result, string message)
    {
        if (result != 0)
        {
            throw new Win32Exception(result, message);
        }
    }

    private static void DispatcherOperation(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            action();
            return;
        }

        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _ = dispatcher.InvokeAsync(action);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public int bInheritHandle;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern int CreatePseudoConsole(COORD size, SafeFileHandle hInput, SafeFileHandle hOutput, uint dwFlags, out IntPtr phPC);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern int ResizePseudoConsole(IntPtr hPC, COORD size);

        [DllImport("kernel32.dll")]
        internal static extern void ClosePseudoConsole(IntPtr hPC);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, IntPtr lpPipeAttributes, int nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateProcess(
            string lpApplicationName,
            string lpCommandLine,
            ref SECURITY_ATTRIBUTES lpProcessAttributes,
            ref SECURITY_ATTRIBUTES lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string lpCurrentDirectory,
            [In] ref STARTUPINFOEX lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll")]
        internal static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);
    }
}
