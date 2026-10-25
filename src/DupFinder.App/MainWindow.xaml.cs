using System.ComponentModel;
using System.Windows;
using DupFinder.App.ViewModels;

namespace DupFinder.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Closed += (_, _) => _vm.Dispose();
    }

    /// <summary>
    /// Пока идёт работа, окно не закрываем сразу: сначала останавливаем её, дожидаемся
    /// (иначе кэш закроется под работающим сканированием) и закрываем уже после.
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
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
        Dispatcher.BeginInvoke(Close);
    }
}
