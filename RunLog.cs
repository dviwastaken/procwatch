using System.IO;
using System.Text;
using System.Text.Json;

namespace ProcWatch;

/// <summary>
/// Olayları satır başına bir JSON nesnesi (JSONL) olarak yazar. Her satır anında diske
/// gider: sandbox aniden kapatılsa da o ana kadarki kayıt hostta durur.
/// </summary>
public sealed class RunLog : IDisposable
{
    readonly StreamWriter? _writer;
    readonly object _gate = new();

    public RunLog(string? path, RunOptions options)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            _writer = new StreamWriter(path, append: true, Encoding.UTF8) { AutoFlush = true };
            WriteRaw(new
            {
                type = "run",
                at = DateTime.Now,
                machine = Environment.MachineName,
                user = Environment.UserName,
                sandbox = options.Sandbox,
                target = options.TargetPath,
                targetArgs = options.TargetArgs
            });
        }
        catch
        {
            _writer = null; // Kayıt tutulamıyorsa izleme yine de sürsün.
        }
    }

    public bool Enabled => _writer is not null;

    public void Write(ProcEvent e)
    {
        WriteRaw(new
        {
            type = e.Started ? "start" : "exit",
            at = e.At,
            pid = e.Node.Pid,
            ppid = e.Node.ParentPid,
            name = e.Node.Name,
            path = e.Node.Path,
            cmd = e.Node.CommandLine,
            signature = e.Node.Signature.ToString(),
            publisher = e.Node.Publisher,
            signatureStatus = e.Node.SignatureStatus,
            targetSubtree = e.Node.IsTargetSubtree
        });
    }

    public void Note(string message, object? extra = null) =>
        WriteRaw(new { type = "note", at = DateTime.Now, message, extra });

    void WriteRaw(object payload)
    {
        if (_writer is null) return;
        try
        {
            var json = JsonSerializer.Serialize(payload);
            lock (_gate) _writer.WriteLine(json);
        }
        catch { }
    }

    public void Dispose()
    {
        lock (_gate) _writer?.Dispose();
    }
}
