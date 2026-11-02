using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace ProcWatch;

public enum SignatureKind { Unknown, Unsigned, Microsoft, ThirdParty }

public readonly record struct SignatureInfo(SignatureKind Kind, string Publisher, string Status);

/// <summary>
/// İmaj dosyalarının Authenticode durumunu çözer.
///
/// Windows sistem ikililerinin çoğu gömülü değil katalog (.cat) imzalıdır; dosyadan
/// sertifika okumak onları "imzasız" gösterir. Bu yüzden asıl yol, tam doğrulama yapan
/// Get-AuthenticodeSignature'a toplu bir çağrıdır (süreç başına değil, parti başına bir
/// powershell). O çalışmazsa gömülü sertifika okumasına düşülür.
/// </summary>
public static class Signing
{
    static readonly ConcurrentDictionary<string, SignatureInfo> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static SignatureInfo Inspect(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new SignatureInfo(SignatureKind.Unknown, "bilinmiyor", "yol okunamadı");

        return Cache.TryGetValue(path, out var hit) ? hit : ReadEmbedded(path);
    }

    /// <summary>Önbellekte olmayan yolları toplu doğrular. Arka planda çağrılmalı.</summary>
    public static void Prime(IEnumerable<string?> paths)
    {
        var pending = paths
            .Where(p => !string.IsNullOrWhiteSpace(p) && !Cache.ContainsKey(p!))
            .Select(p => p!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(File.Exists)
            .ToList();

        if (pending.Count == 0) return;

        try
        {
            VerifyWithPowerShell(pending);
        }
        catch
        {
            // Sessiz düş: aşağıdaki döngü kalanları gömülü sertifikadan çözer.
        }

        foreach (var p in pending)
            Cache.TryAdd(p, ReadEmbedded(p));
    }

    static void VerifyWithPowerShell(List<string> paths)
    {
        var listFile = Path.Combine(Path.GetTempPath(), "procwatch-sig-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllLines(listFile, paths, Encoding.UTF8);

        try
        {
            var script =
                "[Console]::OutputEncoding=[Text.Encoding]::UTF8; " +
                $"$p = Get-Content -LiteralPath '{listFile}' -Encoding UTF8; " +
                "Get-AuthenticodeSignature -LiteralPath $p | " +
                "Select-Object @{n='P';e={$_.Path}},@{n='S';e={[string]$_.Status}}," +
                "@{n='C';e={if ($_.SignerCertificate) { $_.SignerCertificate.Subject } else { '' }}} | " +
                "ConvertTo-Json -Compress -Depth 2";

            var psi = new ProcessStartInfo("powershell.exe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(script);

            using var proc = Process.Start(psi);
            if (proc is null) return;

            var json = proc.StandardOutput.ReadToEnd();
            if (!proc.WaitForExit(90_000) || string.IsNullOrWhiteSpace(json)) return;

            using var doc = JsonDocument.Parse(json);
            var items = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray().ToList()
                : new List<JsonElement> { doc.RootElement.Clone() };

            foreach (var item in items)
            {
                var path = item.TryGetProperty("P", out var pe) ? pe.GetString() : null;
                if (string.IsNullOrEmpty(path)) continue;

                var status = (item.TryGetProperty("S", out var se) ? se.GetString() : null) ?? "Unknown";
                var subject = (item.TryGetProperty("C", out var ce) ? ce.GetString() : null) ?? "";
                Cache[path] = Classify(status, subject);
            }
        }
        finally
        {
            try { File.Delete(listFile); } catch { }
        }
    }

    static SignatureInfo Classify(string status, string subject)
    {
        var publisher = subject.Length > 0 ? ExtractCommonName(subject) : "—";
        var microsoft = subject.Contains("Microsoft Corporation", StringComparison.OrdinalIgnoreCase)
                        || subject.Contains("Microsoft Windows", StringComparison.OrdinalIgnoreCase);

        var kind = status switch
        {
            "Valid" when microsoft => SignatureKind.Microsoft,
            "Valid" => SignatureKind.ThirdParty,
            "NotSigned" => SignatureKind.Unsigned,
            // HashMismatch / NotTrusted / UnknownError: imzalı ama güvenilmez — asla "temiz" sayma.
            _ => SignatureKind.Unknown
        };

        return new SignatureInfo(kind, publisher, status);
    }

    static SignatureInfo ReadEmbedded(string path)
    {
        try
        {
#pragma warning disable SYSLIB0057 // Authenticode sertifikasını okumanın doğrudan yolu.
            using var cert = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            var subject = cert.Subject ?? string.Empty;
            return Classify("Valid", subject) with { Status = "gömülü imza (doğrulanmadı)" };
        }
        catch
        {
            return new SignatureInfo(SignatureKind.Unsigned, "—", "imza bulunamadı");
        }
    }

    static string ExtractCommonName(string subject)
    {
        foreach (var part in subject.Split(',', StringSplitOptions.TrimEntries))
        {
            if (part.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                return part[3..].Trim('"');
        }
        return subject;
    }
}
