using System;
using System.Windows;
using System.Windows.Interop;
using TypeGent.App.ViewModels;

namespace TypeGent.App;

/// <summary>
/// Interaction logic for MainWindow.xaml.
/// <para>
/// All behavior lives in <see cref="MainViewModel"/> (bound as the DataContext). The code-behind is
/// minimal: <see cref="OnSourceInitialized"/> captures our own window handle (so the ViewModel can
/// refuse typing into TypeGent's own window) and hands the HWND to <see cref="HotKeyManager"/> for
/// system-wide hotkey registration; <see cref="OnClosed"/> unregisters it on shutdown.
/// </para>
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        _viewModel.OwnWindowHandle = hwnd;
        _viewModel.InitializeHotKey(hwnd);
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Shutdown();
        base.OnClosed(e);
    }
}
