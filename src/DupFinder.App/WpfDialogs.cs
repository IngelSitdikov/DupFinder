using System.Diagnostics;
using System.IO;
using System.Windows;
using DupFinder.Presentation;
using DupFinder.Presentation.Services;
using Microsoft.Win32;

namespace DupFinder.App;

/// <summary>Диалоги WPF синхронные — результат отдаём уже готовой задачей.</summary>
public sealed class WpfDialogs : IUserDialogs
{
    public Task<string?> PickFolderAsync(string title, string? current)
    {
        var dialog = new OpenFolderDialog { Title = title };
        if (current is not null)
            dialog.InitialDirectory = current;
        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FolderName : null);
    }

    public Task<string?> PickCsvFileAsync(string title, string suggestedName)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = Strings.CsvFilter, FileName = suggestedName };
        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }

    public Task<bool> ShowAsync(string text, string title, DialogKind kind)
    {
        var (buttons, image) = kind switch
        {
            DialogKind.Info => (MessageBoxButton.OK, MessageBoxImage.Information),
            DialogKind.Warning => (MessageBoxButton.OK, MessageBoxImage.Warning),
            DialogKind.OkCancel => (MessageBoxButton.OKCancel, MessageBoxImage.Question),
            _ => (MessageBoxButton.YesNo, MessageBoxImage.Question),
        };
        var result = MessageBox.Show(text, title, buttons, image);
        return Task.FromResult(result is MessageBoxResult.OK or MessageBoxResult.Yes);
    }

    public void Restart()
    {
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true });
        Application.Current.Shutdown();
    }
}
