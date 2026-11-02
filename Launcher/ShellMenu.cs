using Microsoft.Win32;

namespace ProcWatch.Launcher;

/// <summary>
/// "System Monitor ile aç" sağ tık girdisini kaydeder/siler.
///
/// Yalnızca HKEY_CURRENT_USER altına yazar: yönetici gerekmez, yalnızca bu kullanıcıyı
/// etkiler ve tek çağrıyla tamamen geri alınır.
/// </summary>
public static class ShellMenu
{
    const string KeyName = "ProcWatchAnalyze";
    const string Label = "System Monitor ile aç";

    // Sağ tık menüsüne eklenecek dosya türleri (ProgID'ler).
    static readonly string[] ProgIds =
        { "exefile", "batfile", "cmdfile", "scrfile", "JSFile", "VBSFile", "Msi.Package" };

    public static string LauncherPath =>
        Environment.ProcessPath ?? throw new InvalidOperationException("launcher yolu okunamadı");

    public static bool IsInstalled()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\exefile\shell\{KeyName}");
        return key is not null;
    }

    public static void Install()
    {
        var command = $"\"{LauncherPath}\" \"%1\"";

        foreach (var progId in ProgIds)
        {
            using var shell = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}\shell\{KeyName}");
            shell.SetValue(null, Label);
            shell.SetValue("Icon", $"{LauncherPath},0");

            using var cmd = shell.CreateSubKey("command");
            cmd.SetValue(null, command);
        }
    }

    public static void Uninstall()
    {
        foreach (var progId in ProgIds)
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{progId}\shell\{KeyName}", throwOnMissingSubKey: false);
            }
            catch { /* girdi zaten yoksa sorun değil */ }
        }
    }
}
