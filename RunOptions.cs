namespace ProcWatch;

/// <summary>
/// Komut satırı seçenekleri. Sağ tık → "System Monitor ile aç" akışında launcher,
/// sandbox içindeki ProcWatch'ı bu bayraklarla başlatır.
///
///   --target "C:\...\ornek.exe"   izleme kurulduktan sonra çalıştırılacak program
///   --args "..."                  hedefe geçirilecek argümanlar
///   --log "C:\...\events.jsonl"   olayların yazılacağı dosya (hosta geri taşınır)
///   --sandbox                     başlıkta izole ortam uyarısı göster
/// </summary>
public sealed class RunOptions
{
    public string? TargetPath { get; private init; }
    public string? TargetArgs { get; private init; }
    public string? LogPath { get; private init; }
    public bool Sandbox { get; private init; }

    public bool HasTarget => !string.IsNullOrWhiteSpace(TargetPath);

    public static RunOptions Parse(string[] args)
    {
        string? target = null, targetArgs = null, log = null;
        var sandbox = false;

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--target" when i + 1 < args.Length: target = args[++i]; break;
                case "--args" when i + 1 < args.Length: targetArgs = args[++i]; break;
                case "--log" when i + 1 < args.Length: log = args[++i]; break;
                case "--sandbox": sandbox = true; break;
            }
        }

        return new RunOptions
        {
            TargetPath = target,
            TargetArgs = targetArgs,
            LogPath = log,
            Sandbox = sandbox
        };
    }
}
