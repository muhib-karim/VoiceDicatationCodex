using System;
using System.Windows;

namespace VoiceDictationCodex.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Stops the whisper process, releases the microphone and cancels any download when the window closes.
        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
    }
}
