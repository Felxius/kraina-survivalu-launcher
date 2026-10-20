using System.IO;
using System.Windows;
using KrainaSurvivalu.Launcher.App.Testing;
using KrainaSurvivalu.Launcher.App.ViewModels;
using KrainaSurvivalu.Launcher.Core.Updates;

namespace KrainaSurvivalu.Launcher.App;
public partial class App : Application
{
    private MainViewModel? _viewModel;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var smoke = e.Args.Length == 2 && e.Args[0] == "--ui-smoke";
        string? testDirectory = smoke ? Path.GetFullPath(e.Args[1]) : null;
        try
        {
            if (e.Args.Length > 0 && !smoke) throw new ArgumentException("Obsługiwane opcje: --ui-smoke <katalog wyników>.");
            var settingsPath = smoke
                ? Path.Combine(testDirectory!, "sandbox", "settings.json")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KrainaSurvivalu", "Launcher", "settings.json");
            var viewModel = new MainViewModel(settingsPath, smoke ? new RecordingLinkOpener() : null,
                smoke ? new FakeServerQueryService() : null, smoke ? new FakeServerModsQueryService() : null,
                smoke ? new FakeWorkshopClient() : null, smoke ? new FakeLauncherUpdateService() : null,
                smoke ? new RecordingLauncherUpdateApplier().Apply : null,
                smoke ? new UpdateConfiguration() : null, smoke ? new RecordingGameProcessLauncher() : null);
            _viewModel = viewModel;
            await viewModel.InitializeAsync(Path.Combine(AppContext.BaseDirectory, "config", "servers.json"));
            var window = new MainWindow(viewModel);
            MainWindow = window;
            if (smoke)
            {
                var ok = await UiSmoke.RunAsync(window, viewModel, testDirectory!);
                Shutdown(ok ? 0 : 1);
            }
            else
            {
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                window.Show();
                viewModel.StartServerStatusUpdates();
                viewModel.StartLauncherUpdateChecks();
            }
        }
        catch (Exception error)
        {
            if (smoke)
            {
                Directory.CreateDirectory(testDirectory!);
                await File.WriteAllTextAsync(Path.Combine(testDirectory!, "startup-error.txt"), error.ToString());
            }
            else MessageBox.Show("Launcher nie mógł się uruchomić.\n\n" + error.Message, "Kraina Survivalu", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.Dispose();
        base.OnExit(e);
    }
}
