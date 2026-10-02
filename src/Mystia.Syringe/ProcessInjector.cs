using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Mystia.Syringe;

internal static partial class ProcessInjector
{
    private const uint CreateSuspended = 0x00000004;
    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint PageReadWrite = 0x04;

    public static void Start(string gameExe, string bootstrapDll)
    {
        var startup = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>() };
        var workingDirectory = Path.GetDirectoryName(gameExe)!;
        if (!CreateProcess(gameExe, null, nint.Zero, nint.Zero, false, CreateSuspended, nint.Zero, workingDirectory, ref startup, out var process))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcess failed.");

        try
        {
            var pathBytes = Encoding.Unicode.GetBytes(bootstrapDll + "\0");
            var remote = VirtualAllocEx(process.hProcess, nint.Zero, (nuint)pathBytes.Length, MemCommit | MemReserve, PageReadWrite);
            if (remote == nint.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "VirtualAllocEx failed.");

            if (!WriteProcessMemory(process.hProcess, remote, pathBytes, (nuint)pathBytes.Length, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WriteProcessMemory failed.");

            var kernel = GetModuleHandle("kernel32.dll");
            var loadLibrary = GetProcAddress(kernel, "LoadLibraryW");
            if (loadLibrary == nint.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "LoadLibraryW was not found.");

            var thread = CreateRemoteThread(process.hProcess, nint.Zero, nuint.Zero, loadLibrary, remote, 0, out _);
            if (thread == nint.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateRemoteThread failed.");

            WaitForSingleObject(thread, 30_000);
            GetExitCodeThread(thread, out var module);
            CloseHandle(thread);
            if (module == 0)
                throw new InvalidOperationException("The bootstrap DLL did not load. The game was left suspended and will be terminated.");

            if (ResumeThread(process.hThread) == uint.MaxValue)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "ResumeThread failed.");
        }
        catch
        {
            TerminateProcess(process.hProcess, 1);
            throw;
        }
        finally
        {
            CloseHandle(process.hThread);
            CloseHandle(process.hProcess);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int cb;
        public nint lpReserved;
        public nint lpDesktop;
        public nint lpTitle;
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
        public nint lpReserved2;
        public nint hStdInput;
        public nint hStdOutput;
        public nint hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint hProcess;
        public nint hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcess(
        string applicationName,
        string? commandLine,
        nint processAttributes,
        nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        nint environment,
        string currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint VirtualAllocEx(nint process, nint address, nuint size, uint allocationType, uint protect);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WriteProcessMemory(nint process, nint address, byte[] buffer, nuint size, out nuint written);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandle(string name);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint GetProcAddress(nint module, string name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint CreateRemoteThread(
        nint process,
        nint threadAttributes,
        nuint stackSize,
        nint startAddress,
        nint parameter,
        uint creationFlags,
        out uint threadId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint WaitForSingleObject(nint handle, uint milliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetExitCodeThread(nint thread, out uint exitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint ResumeThread(nint thread);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(nint process, uint exitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
