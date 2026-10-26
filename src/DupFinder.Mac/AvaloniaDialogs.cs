using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using DupFinder.Presentation;
using DupFinder.Presentation.Services;

namespace DupFinder.Mac;

/// <summary>Диалоги поверх главного окна. Своего MessageBox в Avalonia нет — окно сообщения собираем здесь же.</summary>
public sealed class AvaloniaDialogs(Window owner) : IUserDialogs
{
    public async Task<string?> PickFolderAsync(string title, string? current)
    {
        var storage = owner.StorageProvider;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            SuggestedStartLocation = current is null ? null : await storage.TryGetFolderFromPathAsync(current),
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickCsvFileAsync(string title, string suggestedName)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = "csv",
            FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }],
        });
        return file?.TryGetLocalPath();
    }

    public async Task<bool> ShowAsync(string text, string title, DialogKind kind)
    {
        var (okText, cancelText) = kind switch
        {
            DialogKind.OkCancel => ("OK", Strings.ButtonCancel),
            DialogKind.YesNo => (Strings.ButtonYes, Strings.ButtonNo),
            _ => ("OK", null),
        };

        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            MaxWidth = 560,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var ok = new Button { Content = okText, IsDefault = true, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Click += (_, _) => dialog.Close(true);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        if (cancelText is not null)
        {
            var cancel = new Button { Content = cancelText, IsCancel = true, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
            cancel.Click += (_, _) => dialog.Close(false);
            buttons.Children.Add(cancel);
        }
        buttons.Children.Add(ok);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 520 },
                buttons,
            },
        };
        return await dialog.ShowDialog<bool?>(owner) == true;
    }

    public void Restart()
    {
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false });
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }
}
