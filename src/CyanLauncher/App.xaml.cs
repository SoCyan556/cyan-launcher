using System.Windows;
using CyanLauncher.Core;

namespace CyanLauncher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, AppSetup.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        if (e.Args.Contains("--uninstall"))
        {
            var paths = new LauncherService().Paths;
            var answer = MessageBox.Show(
                $"Uninstall {AppSetup.AppName}?\n\nYour worlds and settings in\n{paths.Root}\nare kept unless you choose to delete them next.",
                AppSetup.AppName, MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
            if (answer == MessageBoxResult.OK)
            {
                var wipe = MessageBox.Show("Also delete the game folder (worlds, settings, downloaded files)?\n\nChoose No to keep your worlds.",
                    AppSetup.AppName, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
                AppSetup.Uninstall(wipe, paths.Root);
                MessageBox.Show($"{AppSetup.AppName} was removed.", AppSetup.AppName);
            }
            Shutdown();
            return;
        }

        // Setup mode: running the downloaded exe installs the app and opens the installed copy.
        if (!AppSetup.IsInstalledCopy && !AppSetup.IsDevBuild && !e.Args.Contains("--portable"))
        {
            try
            {
                AppSetup.Install();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Setup failed: " + ex.Message, AppSetup.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Shutdown();
            return;
        }

        SelfUpdater.CleanupOldVersion();
        if (AppSetup.IsInstalledCopy)
        {
            try { AppSetup.Register(); } catch (UnauthorizedAccessException) { }
        }
        base.OnStartup(e);
        new MainWindow().Show();
    }
}
