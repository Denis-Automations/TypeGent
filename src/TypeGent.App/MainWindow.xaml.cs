using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TypeGent.Core.HumanTyping;
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

            // Phase 4: drive the human-typing engine with a default profile. Sliders to tune
            // WPM / typo rate arrive in Phase 5; for now the profile defaults are fixed.
            var profile = new TypingProfile();
            var engine = new HumanTypingEngine(new Random());
            var actions = engine.Plan(text, profile, _layout);
            await _orchestrator.RunAsync(actions, CancellationToken.None);

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
