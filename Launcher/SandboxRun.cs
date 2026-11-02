using System.Diagnostics;
using System.IO;
using System.Xml.Linq;

namespace ProcWatch.Launcher;

/// <summary>
/// Bir dosyayı Windows Sandbox içinde, ProcWatch izlemesi altında çalıştırır.
///
/// Yerleşim: launcher'ın yanındaki <c>tool\</c> klasöründe ProcWatch'ın kendine yeten
/// (self-contained) sürümü durur. Her çalıştırma için ayrı bir çalışma klasörü açılır:
///   payload\  hedefin kopyası   → sandbox'a salt okunur bağlanır
///   report\   olay kaydı        → sandbox'a yazılabilir bağlanır (tek dışarı kanal)
/// </summary>
public sealed class SandboxRun
{
    public static string SandboxExe =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsSandbox.exe");

    public static bool SandboxAvailable => File.Exists(SandboxExe);

    public static string ToolDirectory =>
        Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!) ?? ".", "tool");

    public static bool ToolAvailable => File.Exists(Path.Combine(ToolDirectory, "ProcWatch.exe"));

    public string TargetPath { get; }
    public bool AllowNetwork { get; init; }

    /// <summary>Hedefin klasöründeki diğer dosyaları da al (yanında DLL'i olan programlar için).</summary>
    public bool CopySiblingFiles { get; init; }

    /// <summary>Klasör kopyalamada üst sınır; yanlışlıkla dev bir dizini taşımayı engeller.</summary>
    public const long SiblingSizeLimitBytes = 512L * 1024 * 1024;
    public string RunDirectory { get; }
    public string ReportDirectory => Path.Combine(RunDirectory, "report");
    public string LogPath => Path.Combine(ReportDirectory, "events.jsonl");

    public SandboxRun(string targetPath)
    {
        TargetPath = targetPath;
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var safeName = Path.GetFileNameWithoutExtension(targetPath);
        foreach (var c in Path.GetInvalidFileNameChars()) safeName = safeName.Replace(c, '_');

        RunDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProcWatch", "runs", $"{stamp}-{safeName}");
    }

    public void Start()
    {
        var wsbPath = Prepare();
        Process.Start(new ProcessStartInfo(SandboxExe, $"\"{wsbPath}\"") { UseShellExecute = true });
    }

    /// <summary>Çalışma klasörünü hazırlar ve .wsb dosyasını yazar; sandbox'ı başlatmaz.</summary>
    public string Prepare()
    {
        var payloadDir = Path.Combine(RunDirectory, "payload");
        Directory.CreateDirectory(payloadDir);
        Directory.CreateDirectory(ReportDirectory);

        var fileName = Path.GetFileName(TargetPath);
        File.Copy(TargetPath, Path.Combine(payloadDir, fileName), overwrite: true);

        if (CopySiblingFiles) CopySiblings(payloadDir);

        const string sandboxRoot = @"C:\analiz";
        var sandboxTarget = $@"{sandboxRoot}\payload\{fileName}";
        var command =
            $@"{sandboxRoot}\tool\ProcWatch.exe --sandbox " +
            $"--target \"{sandboxTarget}\" " +
            $"--log \"{sandboxRoot}\\report\\events.jsonl\"";

        var config = new XDocument(
            new XElement("Configuration",
                new XElement("VGpu", "Disable"),
                new XElement("Networking", AllowNetwork ? "Default" : "Disable"),
                new XElement("AudioInput", "Disable"),
                new XElement("VideoInput", "Disable"),
                new XElement("PrinterRedirection", "Disable"),
                new XElement("ClipboardRedirection", "Disable"),
                new XElement("ProtectedClient", "Enable"),
                new XElement("MemoryInMB", "4096"),
                new XElement("MappedFolders",
                    Mapped(ToolDirectory, $@"{sandboxRoot}\tool", readOnly: true),
                    Mapped(payloadDir, $@"{sandboxRoot}\payload", readOnly: true),
                    Mapped(ReportDirectory, $@"{sandboxRoot}\report", readOnly: false)),
                new XElement("LogonCommand",
                    new XElement("Command", command))));

        var wsbPath = Path.Combine(RunDirectory, "run.wsb");
        config.Save(wsbPath);
        return wsbPath;
    }

    /// <summary>
    /// Hedefin klasöründeki diğer dosyaları payload'a kopyalar. Yalnızca üst seviye dosyalar;
    /// toplam boyut sınırı aşılırsa kopyalama olduğu yerde durur (hedef zaten kopyalanmıştır).
    /// </summary>
    void CopySiblings(string payloadDir)
    {
        var sourceDir = Path.GetDirectoryName(TargetPath);
        if (string.IsNullOrEmpty(sourceDir)) return;

        long copied = 0;
        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            if (string.Equals(file, TargetPath, StringComparison.OrdinalIgnoreCase)) continue;

            var info = new FileInfo(file);
            if (copied + info.Length > SiblingSizeLimitBytes) break;

            try
            {
                File.Copy(file, Path.Combine(payloadDir, info.Name), overwrite: true);
                copied += info.Length;
            }
            catch { /* kilitli ya da okunamayan dosyayı atla */ }
        }
    }

    static XElement Mapped(string host, string sandbox, bool readOnly) =>
        new("MappedFolder",
            new XElement("HostFolder", host),
            new XElement("SandboxFolder", sandbox),
            new XElement("ReadOnly", readOnly ? "true" : "false"));
}
