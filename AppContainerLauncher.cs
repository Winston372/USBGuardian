using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace USBGuardian.App.Services
{
    /// <summary>
    /// Fail-closed AppContainer launch. No Process.Start fallback is permitted.
    /// Windows 10/11 Home supports the AppContainer APIs used here.
    /// The worker itself verifies TokenIsAppContainer before analysis.
    /// </summary>
    internal static class AppContainerLauncher
    {
        private const string ProfileName = "USBGuardian.SafeLab.Worker.v1";
        private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
        private const uint CREATE_NO_WINDOW = 0x08000000;
        private const uint CREATE_SUSPENDED = 0x00000004;
        private const uint PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES = 0x00020009;
        private const uint JOB_OBJECT_LIMIT_PROCESS_MEMORY = 0x00000100;
        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
        private const int JobObjectExtendedLimitInformation = 9;
        private const uint WAIT_OBJECT_0 = 0;

        public static void Run(string analyzerExe, string outputPath, byte[] bytes, string? previewInput = null)
        {
            if (bytes.Length > 2048) throw new ArgumentException("過長的分析資料");
            if (!File.Exists(analyzerExe)) throw new FileNotFoundException("找不到 SafeLab Analyzer", analyzerExe);
            IntPtr sidPointer = IntPtr.Zero, attributes = IntPtr.Zero, capabilities = IntPtr.Zero;
            IntPtr job = IntPtr.Zero;
            IntPtr size = IntPtr.Zero;
            bool attributesInitialized = false;
            try
            {
                int hr = CreateAppContainerProfile(ProfileName, "USB Guardian SafeLab",
                    "Restricted USB file metadata analyzer", IntPtr.Zero, 0, out sidPointer);
                if (hr == unchecked((int)0x800700B7)) // ERROR_ALREADY_EXISTS
                    hr = DeriveAppContainerSidFromAppContainerName(ProfileName, out sidPointer);
                if (hr != 0 || sidPointer == IntPtr.Zero)
                    throw new InvalidOperationException($"無法建立 AppContainer: HRESULT 0x{hr:X8}");

                SecurityIdentifier sid = new SecurityIdentifier(sidPointer);
                GrantWorkerReadAndExecute(Path.GetDirectoryName(analyzerExe)!, sid);
                // Allow traversal of the app-specific parent, but do not grant
                // recursive access to any unrelated user files.
                GrantDirectoryTraverse(Path.GetDirectoryName(Path.GetDirectoryName(outputPath)!)!, sid);
                GrantResultsAccess(Path.GetDirectoryName(outputPath)!, outputPath, sid);
                if (previewInput != null)
                    GrantPreviewRead(previewInput, sid);

                // Configure a job object before allowing the suspended process to run.
                job = CreateJobObject(IntPtr.Zero, null);
                if (job == IntPtr.Zero) throw Win32("CreateJobObject");
                JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits = new();
                limits.Basic.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE | JOB_OBJECT_LIMIT_PROCESS_MEMORY;
                limits.ProcessMemoryLimit = new UIntPtr(256UL * 1024UL * 1024UL);
                if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation,
                    ref limits, Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
                    throw Win32("SetInformationJobObject");

                // The 2-call allocation protocol for an extended attribute list.
                InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
                if (size == IntPtr.Zero) throw Win32("InitializeProcThreadAttributeList(size)");
                attributes = Marshal.AllocHGlobal(size);
                if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref size))
                    throw Win32("InitializeProcThreadAttributeList");
                attributesInitialized = true;

                SECURITY_CAPABILITIES security = new()
                {
                    AppContainerSid = sidPointer, Capabilities = IntPtr.Zero,
                    CapabilityCount = 0, Reserved = 0
                };
                capabilities = Marshal.AllocHGlobal(Marshal.SizeOf<SECURITY_CAPABILITIES>());
                Marshal.StructureToPtr(security, capabilities, false);
                if (!UpdateProcThreadAttribute(attributes, 0,
                    new IntPtr(PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES), capabilities,
                    (IntPtr)Marshal.SizeOf<SECURITY_CAPABILITIES>(), IntPtr.Zero, IntPtr.Zero))
                    throw Win32("UpdateProcThreadAttribute");

                STARTUPINFOEX startup = new();
                startup.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
                startup.lpAttributeList = attributes;
                string b64 = Convert.ToBase64String(bytes);
                StringBuilder command = new StringBuilder($"\"{analyzerExe}\" --out \"{outputPath}\" --data {b64}");
                if (previewInput != null)
                    command.Append($" --image \"{previewInput}\"");
                if (!CreateProcessW(analyzerExe, command, IntPtr.Zero, IntPtr.Zero, false,
                    EXTENDED_STARTUPINFO_PRESENT | CREATE_NO_WINDOW | CREATE_SUSPENDED,
                    IntPtr.Zero, Path.GetDirectoryName(analyzerExe), ref startup,
                    out PROCESS_INFORMATION process))
                    throw Win32("CreateProcess(AppContainer)");
                try
                {
                    if (!AssignProcessToJobObject(job, process.hProcess))
                    {
                        TerminateProcess(process.hProcess, 1);
                        throw Win32("AssignProcessToJobObject");
                    }
                    if (ResumeThread(process.hThread) == uint.MaxValue)
                    {
                        TerminateProcess(process.hProcess, 1);
                        throw Win32("ResumeThread");
                    }
                    uint wait = WaitForSingleObject(process.hProcess, 10000);
                    if (wait != WAIT_OBJECT_0)
                    {
                        TerminateProcess(process.hProcess, 1);
                        throw new TimeoutException("隔離分析超過 10 秒，程序已停止");
                    }
                    if (!GetExitCodeProcess(process.hProcess, out uint code))
                        throw Win32("GetExitCodeProcess");
                    if (code != 0)
                        throw new InvalidOperationException($"隔離分析程序未通過權限驗證或執行失敗（Code {code}）。");
                }
                finally
                {
                    CloseHandle(process.hThread);
                    CloseHandle(process.hProcess);
                }
            }
            finally
            {
                if (job != IntPtr.Zero) CloseHandle(job); // KILL_ON_JOB_CLOSE
                if (attributesInitialized) DeleteProcThreadAttributeList(attributes);
                if (attributes != IntPtr.Zero) Marshal.FreeHGlobal(attributes);
                if (capabilities != IntPtr.Zero) Marshal.FreeHGlobal(capabilities);
                if (sidPointer != IntPtr.Zero) FreeSid(sidPointer);
            }
        }

        private static void GrantWorkerReadAndExecute(string folder, SecurityIdentifier sid)
        {
            DirectoryInfo info = new(folder);
            DirectorySecurity security = info.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(sid,
                FileSystemRights.ReadAndExecute | FileSystemRights.ListDirectory,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            info.SetAccessControl(security);
        }

        private static void GrantDirectoryTraverse(string folder, SecurityIdentifier sid)
        {
            DirectoryInfo parent = new(folder);
            DirectorySecurity security = parent.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(sid,
                FileSystemRights.Traverse | FileSystemRights.ReadAttributes,
                InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
            parent.SetAccessControl(security);
        }

        private static void GrantResultsAccess(string folder, string output, SecurityIdentifier sid)
        {
            DirectoryInfo parent = new(folder);
            DirectorySecurity directorySecurity = parent.GetAccessControl();
            directorySecurity.AddAccessRule(new FileSystemAccessRule(sid,
                FileSystemRights.ReadAndExecute | FileSystemRights.ListDirectory,
                InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
            parent.SetAccessControl(directorySecurity);
            FileInfo file = new(output);
            FileSecurity security = file.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(sid,
                FileSystemRights.Read | FileSystemRights.Write,
                AccessControlType.Allow));
            file.SetAccessControl(security);
        }
        private static void GrantPreviewRead(string input, SecurityIdentifier sid)
        {
            // Grant READ to the individual temporary image, not the USB or parent user folders.
            FileInfo file = new(input);
            FileSecurity security = file.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(sid,
                FileSystemRights.Read, AccessControlType.Allow));
            file.SetAccessControl(security);
        }
        private static Win32Exception Win32(string operation) => new(Marshal.GetLastWin32Error(), operation + " 失敗");

        [StructLayout(LayoutKind.Sequential)]
        private struct SECURITY_CAPABILITIES
        { public IntPtr AppContainerSid, Capabilities; public int CapabilityCount, Reserved; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb; public string? lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFOEX { public STARTUPINFO StartupInfo; public IntPtr lpAttributeList; }
        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }
        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }
        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION Basic;
            public IO_COUNTERS Io;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        [DllImport("userenv.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int CreateAppContainerProfile(string name, string display, string description,
            IntPtr capabilities, uint capabilityCount, out IntPtr sid);
        [DllImport("userenv.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int DeriveAppContainerSidFromAppContainerName(string name, out IntPtr sid);
        [DllImport("advapi32.dll")]
        private static extern IntPtr FreeSid(IntPtr sid);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value,
            IntPtr size, IntPtr previous, IntPtr returnSize);
        [DllImport("kernel32.dll")]
        private static extern void DeleteProcThreadAttributeList(IntPtr list);
        [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcessW(string applicationName, StringBuilder commandLine,
            IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles,
            uint creationFlags, IntPtr environment, string? currentDirectory,
            ref STARTUPINFOEX startupInfo, out PROCESS_INFORMATION processInformation);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr job, int infoClass,
            ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits, int length);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(IntPtr thread);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateProcess(IntPtr process, uint exitCode);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeProcess(IntPtr process, out uint code);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
