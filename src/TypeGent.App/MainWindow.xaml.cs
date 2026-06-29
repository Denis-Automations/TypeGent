using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TypeGent.Core.Layouts;
using TypeGent.Core.Typing;

namespace TypeGent.App;

/// <summary>
/// Interaction logic for MainWindow.xaml.
/// <para>
/// Phase 3 is a debug harness: a "Type test" button counts down (so the user can focus the
/// target window), then types the text box contents into the foreground window via the
/// orchestrator + layout. The real UI arrives in Phase 5.
/// </para>
/// </summary>
public partial class MainWindow : Window
{
    private readonly TypingOrchestrator _orchestrator;
    private readonly KeyboardLayout _layout;

    public MainWindow(TypingOrchestrator orchestrator, KeyboardLayout layout)
    {
        _orchestrator = orchestrator;
        _layout = layout;
        InitializeComponent();
    }

    private async void TypeButton_Click(object sender, RoutedEventArgs e)
    {
        var text = TextToType.Text;
        TypeButton.IsEnabled = false;
        try
        {
            for (var i = 3; i >= 1; i--)
            {
                StatusText.Text = $"Switch to your target window… typing in {i}";
                await Task.Delay(TimeSpan.FromSeconds(1));
            }

            StatusText.Text = "Typing…";

            // ~40 ms/char keeps it observable and reliable; the realistic timing model is Phase 4.
            await _orchestrator.RunTextAsync(
                text, _layout, TimeSpan.FromMilliseconds(40), CancellationToken.None);

            StatusText.Text = "Done.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error: " + ex.Message;
        }
        finally
        {
            TypeButton.IsEnabled = true;
        }
    }
}
