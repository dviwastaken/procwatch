namespace ProcWatch;

/// <summary>
/// "Sadece önemli" filtresinin karar mantığı. Amaç, Windows'un kendi gürültüsünü
/// bastırıp geriye kullanıcı/uygulama kaynaklı süreçleri bırakmak.
/// </summary>
public static class Importance
{
    static readonly string WindowsDir =
        Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd(System.IO.Path.DirectorySeparatorChar);

    static readonly HashSet<string> NoiseNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "svchost.exe", "conhost.exe", "dllhost.exe", "runtimebroker.exe", "csrss.exe",
        "wininit.exe", "services.exe", "lsass.exe", "fontdrvhost.exe", "sihost.exe",
        "taskhostw.exe", "ctfmon.exe", "smartscreen.exe", "searchindexer.exe",
        "searchprotocolhost.exe", "searchfilterhost.exe", "wmiprvse.exe", "audiodg.exe",
        "backgroundtaskhost.exe", "shellexperiencehost.exe", "startmenuexperiencehost.exe",
        "textinputhost.exe", "spoolsv.exe", "dwm.exe", "winlogon.exe", "smss.exe",
        "lsaiso.exe", "securityhealthservice.exe", "securityhealthsystray.exe",
        "wudfhost.exe", "dasHost.exe", "sppsvc.exe", "systemsettingsbroker.exe",
        "usocoreworker.exe", "widgets.exe", "widgetservice.exe", "lockapp.exe",
        "applicationframehost.exe", "system", "registry", "memory compression", "idle"
    };

    /// <param name="startedThisSession">İzleme başladıktan sonra doğan süreç: her zaman önemli.</param>
    public static bool IsImportant(RawProc p, SignatureInfo signature, bool startedThisSession)
    {
        if (startedThisSession) return true;

        // Bozuk ya da güvenilmeyen imza her zaman bakılmaya değer.
        if (signature.Status is "HashMismatch" or "NotTrusted") return true;

        var inWindows = p.Path is not null &&
                        p.Path.StartsWith(WindowsDir, StringComparison.OrdinalIgnoreCase);

        // Yolu okunamayan süreçler korunur: gizlenmesi gereken değil, bakılması gereken şeyler.
        // Yolu okunamayan süreçler: yönetici olmadan başka oturumların/yükseltilmiş
        // süreçlerin imajına erişilemez. Oturum 0 servis alanıdır ve doğrulanamadığı için
        // gürültü sayılır; kullanıcı oturumundaki doğrulanamayan süreçler görünür kalır.
        if (p.Path is null) return p.SessionId != 0 && !NoiseNames.Contains(p.Name);

        // Windows dışındaki her şey kullanıcı/uygulama alanıdır: görünür kalır.
        if (!inWindows) return true;

        // Microsoft imzası doğrulanmış sistem ikilisi = gürültü.
        if (signature.Kind == SignatureKind.Microsoft) return false;

        // Sistem dizininde ama Microsoft imzalı değil: tam da bakılması gereken durum.
        // (İmza doğrulaması hiç çalışmadıysa bilinen gürültü adları yine elenir.)
        return !NoiseNames.Contains(p.Name);
    }
}
