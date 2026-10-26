using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using DupFinder.Presentation.ViewModels;

namespace DupFinder.Mac;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel(new AvaloniaDialogs(this));
        DataContext = _vm;
        Closed += (_, _) => _vm.Dispose();
    }

    /// <summary>
    /// Пока идёт работа, окно не закрываем сразу: сначала останавливаем её, дожидаемся
    /// (иначе кэш закроется под работающим сканированием) и закрываем уже после.
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_vm.IsBusy)
        {
            e.Cancel = true;
            _vm.CancelCommand.Execute(null);
            _vm.PropertyChanged += CloseWhenIdle;
        }
        base.OnClosing(e);
    }

    private void CloseWhenIdle(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.IsBusy) || _vm.IsBusy)
            return;
        _vm.PropertyChanged -= CloseWhenIdle;
        Dispatcher.UIThread.Post(Close);
    }

    private void OnFileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: FileItem file })
            _vm.ShowInFileManagerCommand.Execute(file);
    }
}
