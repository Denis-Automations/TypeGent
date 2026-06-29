using System;
using System.Windows;
using System.Windows.Interop;
using TypeGent.App.ViewModels;

namespace TypeGent.App;

/// <summary>
/// Interaction logic for MainWindow.xaml.
/// <para>
/// Phase 5: all behavior lives in <see cref="MainViewModel"/> (bound as the DataContext). The only
/// code-behind is capturing our own window handle in <see cref="OnSourceInitialized"/> — the
/// ViewModel uses it to refuse typing into TypeGent's own window. (The system-wide hotkey
/// registration that also hooks in here arrives in Phase 6.)
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
        _viewModel.OwnWindowHandle = new WindowInteropHelper(this).Handle;
    }
}
