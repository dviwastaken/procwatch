using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace ProcWatch;

public partial class MainWindow : Window
{
    readonly ProcessTree _tree = new();
    readonly ObservableCollection<string> _events = new();
    readonly DispatcherTimer _timer = new();
    readonly RunOptions _options;
    readonly RunLog _log;
    bool _busy;
    bool _targetLaunched;

    public MainWindow()
    {
        InitializeComponent();
        _options = RunOptions.Parse(Environment.GetCommandLineArgs());
        _log = new RunLog(_options.LogPath, _options);

        Tree.ItemsSource = _tree.Roots;
        EventList.ItemsSource = _events;

        // Hedef analizinde kısa ömürlü süreçleri kaçırmamak için daha sık yokla.
        _timer.Interval = TimeSpan.FromMilliseconds(_options.HasTarget ? 750 : 1500);

        SetUpTargetUi();

        CountText.Text = "yükleniyor — imzalar doğrulanıyor…";
        Loaded += async (_, _) => await Poll();
        _timer.Tick += async (_, _) => await Poll();
        _timer.Start();

        Closed += (_, _) =>
        {
            _timer.Stop();
            _log.Note("izleme durduruldu");
            _log.Dispose();
        };
    }

    void SetUpTargetUi()
    {
        if (_options.Sandbox)
        {
            Banner.Visibility = Visibility.Visible;
            BannerText.Text = "İZOLE ORTAM — bu pencere tek kullanımlık sanal makinede çalışıyor; "
                              + "burada olan hiçbir şey ana makineye yansımaz.";
        }

        if (!_options.HasTarget) return;

        OnlyTarget.Visibility = Visibility.Visible;
        OnlyTarget.IsChecked = true;
        Title = $"ProcWatch — {Path.GetFileName(_options.TargetPath)} analizi";
    }

    async Task Poll()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            // Anlık görüntü + imza doğrulaması UI iş parçacığını kilitlemesin.
            var snapshot = await Task.Run(() =>
            {
                var s = ProcessSource.Snapshot();
                Signing.Prime(s.Values.Select(v => v.Path)); // imza doğrulaması parti halinde
                return s;
            });
            var newEvents = _tree.Update(snapshot);

            foreach (var e in newEvents)
            {
                _events.Insert(0, e.Line);
                _log.Write(e);
            }
            while (_events.Count > 300)
                _events.RemoveAt(_events.Count - 1);

            ApplyFilter();
            CountText.Text = $"{_tree.LiveCount} canlı · {_tree.TotalCount} toplam"
                             + (_log.Enabled ? " · kaydediliyor" : "");
            SourceText.Text = ProcessSource.LastReadUsedWmi
                ? "kaynak: WMI (komut satırı dahil)"
                : "kaynak: Toolhelp32 (WMI kapalı — komut satırı yok)";

            // Temel çizgi kurulduktan sonra hedefi başlat: öncesi/sonrası ayrımı net olsun.
            if (!_targetLaunched && _options.HasTarget)
            {
                _targetLaunched = true;
                LaunchTarget();
                _busy = false;
                await Poll(); // hedefi hemen yakala, sonraki turu bekleme
                return;
            }
        }
        catch (Exception ex)
        {
            SourceText.Text = "okuma hatası: " + ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    void LaunchTarget()
    {
        var path = _options.TargetPath!;
        try
        {
            var psi = new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory
            };
            if (!string.IsNullOrWhiteSpace(_options.TargetArgs))
                psi.Arguments = _options.TargetArgs;

            var proc = Process.Start(psi);
            if (proc is not null)
            {
                _tree.TargetPids.Add(proc.Id);
                _log.Note("hedef başlatıldı", new { pid = proc.Id, path });
                _events.Insert(0, $"{DateTime.Now:HH:mm:ss}  >  hedef başlatıldı: {Path.GetFileName(path)} ({proc.Id})");
            }
        }
        catch (Exception ex)
        {
            _log.Note("hedef başlatılamadı", new { path, error = ex.Message });
            _events.Insert(0, $"{DateTime.Now:HH:mm:ss}  !  hedef başlatılamadı: {ex.Message}");
        }
    }

    void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        FilterHint.Visibility = FilterBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilter();
    }

    void ApplyFilter() =>
        _tree.ApplyFilter(
            FilterBox.Text,
            OnlyImportant.IsChecked == true,
            ShowExited.IsChecked == true,
            OnlyTarget.IsChecked == true);
}
