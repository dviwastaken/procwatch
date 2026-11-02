using System.Windows;

namespace ProcWatch.Launcher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Kurulum/kaldırma pencere açmadan biter.
        if (e.Args.Length > 0 && e.Args[0] is "--install" or "--uninstall")
        {
            var install = e.Args[0] == "--install";
            try
            {
                if (install) ShellMenu.Install(); else ShellMenu.Uninstall();
                MessageBox.Show(
                    install
                        ? "\"System Monitor ile aç\" sağ tık menüsüne eklendi."
                        : "Sağ tık menüsü girdisi kaldırıldı.",
                    "ProcWatch", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("İşlem başarısız: " + ex.Message, "ProcWatch",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Shutdown();
            return;
        }

        var target = e.Args.Length > 0 && !e.Args[0].StartsWith("--") ? e.Args[0] : null;
        new MainWindow(target).Show();
    }
}
