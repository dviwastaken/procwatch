using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;

namespace ProcWatch.Launcher;

public partial class MainWindow : Window
{
    readonly string? _target;
    SandboxRun? _run;

    public MainWindow(string? target)
    {
        InitializeComponent();
        _target = target;

        if (_target is null) SetUpSettingsMode();
        else SetUpAnalysisMode();
    }

    // ---------- Sağ tık girdisi yönetimi ----------

    void SetUpSettingsMode()
    {
        Heading.Text = "System Monitor";
        RefreshSettingsText();
        SecondaryButton.Content = "Kapat";
    }

    void RefreshSettingsText()
    {
        var installed = ShellMenu.IsInstalled();
        Subheading.Text = installed
            ? "Sağ tık menüsündeki \"System Monitor ile aç\" girdisi kurulu. Bir programa sağ tıkla, izole ortamda çalışsın."
            : "Bir programa sağ tıklayıp izole ortamda çalıştırabilmek için menü girdisini ekle. "
              + "Kayıt yalnızca bu kullanıcı için yapılır (HKCU), yönetici gerekmez, tek tıkla geri alınır.";
        PrimaryButton.Content = installed ? "Menüden kaldır" : "Sağ tık menüsüne ekle";
    }

    // ---------- Analiz ----------

    void SetUpAnalysisMode()
    {
        Heading.Text = "İzole analiz";
        Subheading.Text = "Program tek kullanımlık bir sanal makinede çalışacak ve ProcWatch onu izleyecek. "
                          + "Ana makinedeki dosyalara, kayıt defterine veya süreçlere erişemez.";

        FileCard.Visibility = Visibility.Visible;
        AllowNetwork.Visibility = Visibility.Visible;
        NetworkHint.Visibility = Visibility.Visible;
        CopySiblings.Visibility = Visibility.Visible;
        CopyHint.Visibility = Visibility.Visible;
        SecondaryButton.Content = "İptal";

        FileName.Text = Path.GetFileName(_target);
        FilePath.Text = _target;
        FileFacts.Text = "inceleniyor…";

        if (!File.Exists(_target))
        {
            ShowWarning("Dosya bulunamadı.", null);
            PrimaryButton.IsEnabled = false;
            return;
        }

        if (!SandboxRun.SandboxAvailable)
        {
            ShowWarning(
                "Windows Sandbox kurulu değil. Yükseltilmiş bir PowerShell'de aşağıdaki komutu bir kez çalıştırıp "
                + "bilgisayarı yeniden başlat, sonra buraya dön.",
                "Enable-WindowsOptionalFeature -Online -FeatureName Containers-DisposableClientVM -All");
            PrimaryButton.IsEnabled = false;
        }
        else if (!SandboxRun.ToolAvailable)
        {
            ShowWarning(
                $"İzleyici bulunamadı: {SandboxRun.ToolDirectory}. Launcher'ın yanında ProcWatch'ın "
                + "kendine yeten sürümünü içeren bir tool klasörü olmalı (publish.ps1 bunu üretir).",
                null);
            PrimaryButton.IsEnabled = false;
        }

        _ = DescribeFileAsync();
    }

    async Task DescribeFileAsync()
    {
        var path = _target!;
        var (size, hash, signature) = await Task.Run(() =>
        {
            var length = new FileInfo(path).Length;

            string sha;
            using (var stream = File.OpenRead(path))
                sha = Convert.ToHexString(SHA256.HashData(stream));

            Signing.Prime(new[] { path });
            return (length, sha, Signing.Inspect(path));
        });

        FileFacts.Text = $"{size / 1024.0 / 1024.0:0.##} MB  ·  imza: {signature.Publisher} ({signature.Status})";
        FileHash.Text = "SHA-256  " + hash;
    }

    void ShowWarning(string text, string? command)
    {
        Warning.Visibility = Visibility.Visible;
        WarningText.Text = text;
        if (command is null) return;

        WarningCommand.Visibility = Visibility.Visible;
        WarningCommand.Text = command;
    }

    // ---------- Butonlar ----------

    void OnPrimary(object sender, RoutedEventArgs e)
    {
        if (_target is null)
        {
            try
            {
                if (ShellMenu.IsInstalled()) ShellMenu.Uninstall();
                else ShellMenu.Install();
                RefreshSettingsText();
            }
            catch (Exception ex)
            {
                MessageBox.Show("İşlem başarısız: " + ex.Message, "System Monitor",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return;
        }

        if (_run is not null) // ikinci tık: raporu aç
        {
            Process.Start(new ProcessStartInfo(_run.ReportDirectory) { UseShellExecute = true });
            return;
        }

        try
        {
            _run = new SandboxRun(_target)
            {
                AllowNetwork = AllowNetwork.IsChecked == true,
                CopySiblingFiles = CopySiblings.IsChecked == true
            };
            _run.Start();

            Status.Visibility = Visibility.Visible;
            Status.Text = "Sanal makine başlatıldı. İzleme penceresi orada açılacak, program ondan sonra çalışacak.\n"
                          + "Olay kaydı: " + _run.LogPath;
            PrimaryButton.Content = "Rapor klasörünü aç";
            AllowNetwork.IsEnabled = false;
            CopySiblings.IsEnabled = false;
            SecondaryButton.Content = "Kapat";
        }
        catch (Exception ex)
        {
            ShowWarning("Sanal makine başlatılamadı: " + ex.Message, null);
        }
    }

    void OnSecondary(object sender, RoutedEventArgs e) => Close();
}
