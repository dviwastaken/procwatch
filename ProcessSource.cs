using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

namespace ProcWatch;

/// <summary>Tek bir anlık görüntüde okunan ham süreç kaydı.</summary>
public sealed record RawProc(
    int Pid,
    int ParentPid,
    string Name,
    string? Path,
    string? CommandLine,
    DateTime? StartTime,
    uint SessionId);

/// <summary>
/// Süreç listesini WMI üzerinden okur (komut satırı + başlangıç zamanı için).
/// WMI erişilemezse Toolhelp32 anlık görüntüsüne düşer; o zaman komut satırı olmaz.
/// </summary>
public static class ProcessSource
{
    public static bool LastReadUsedWmi { get; private set; }

    public static Dictionary<int, RawProc> Snapshot()
    {
        try
        {
            var result = ReadFromWmi();
            LastReadUsedWmi = true;
            return result;
        }
        catch
        {
            LastReadUsedWmi = false;
            return ReadFromToolhelp();
        }
    }

    static Dictionary<int, RawProc> ReadFromWmi()
    {
        var map = new Dictionary<int, RawProc>();
        using var searcher = new ManagementObjectSearcher(
            "SELECT ProcessId,ParentProcessId,Name,ExecutablePath,CommandLine,CreationDate,SessionId FROM Win32_Process");
        using var items = searcher.Get();

        foreach (ManagementObject mo in items)
        {
            using (mo)
            {
                var pid = (int)Convert.ToUInt32(mo["ProcessId"]);
                var ppid = (int)Convert.ToUInt32(mo["ParentProcessId"] ?? 0u);
                DateTime? start = null;
                if (mo["CreationDate"] is string cim && cim.Length > 0)
                {
                    try { start = ManagementDateTimeConverter.ToDateTime(cim); } catch { }
                }

                map[pid] = new RawProc(
                    pid,
                    ppid,
                    (string?)mo["Name"] ?? $"pid {pid}",
                    (string?)mo["ExecutablePath"],
                    (string?)mo["CommandLine"],
                    start,
                    Convert.ToUInt32(mo["SessionId"] ?? 0u));
            }
        }

        return map;
    }

    static Dictionary<int, RawProc> ReadFromToolhelp()
    {
        var map = new Dictionary<int, RawProc>();
        var snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return map;

        try
        {
            var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            if (!Process32FirstW(snap, ref entry)) return map;

            do
            {
                var pid = (int)entry.th32ProcessID;
                string? path = null;
                DateTime? start = null;
                try
                {
                    using var p = Process.GetProcessById(pid);
                    start = p.StartTime;
                    path = p.MainModule?.FileName;
                }
                catch { /* erişim reddi ya da süreç kapandı */ }

                map[pid] = new RawProc(pid, (int)entry.th32ParentProcessID, entry.szExeFile, path, null, start, 0);
            }
            while (Process32NextW(snap, ref entry));
        }
        finally
        {
            CloseHandle(snap);
        }

        return map;
    }

    const uint TH32CS_SNAPPROCESS = 0x00000002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool Process32FirstW(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool Process32NextW(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr hObject);
}
