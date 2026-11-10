using System;
using System.Windows;

namespace VoiceDictationCodex.Services;

/// <summary>Runs work on the UI thread. View-model state and commands must only change there.</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}

public sealed class WpfUiDispatcher : IUiDispatcher
{
    public void Post(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }
}

public interface IDialogService
{
    void ShowInfo(string message, string title);
}

public sealed class MessageBoxDialogService : IDialogService
{
    public void ShowInfo(string message, string title)
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
}
