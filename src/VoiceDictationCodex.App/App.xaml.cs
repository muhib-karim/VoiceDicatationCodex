using System.Threading.Tasks;
using System.Windows;
using VoiceDictationCodex.Services;

namespace VoiceDictationCodex;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Last line of defence: log the failure and keep the window (and the transcript) alive.
        DispatcherUnhandledException += (_, args) =>
        {
            CrashLog.Write(args.Exception);
            MessageBox.Show(
                $"Something went wrong: {args.Exception.Message}",
                "Voice Dictation Codex",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CrashLog.Write(args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);
    }
}
