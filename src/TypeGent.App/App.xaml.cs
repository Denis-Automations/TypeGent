using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TypeGent.Core.Abstractions;
using TypeGent.Core.Typing;
using TypeGent.Native;

namespace TypeGent.App;

/// <summary>
/// Interaction logic for App.xaml. Builds the DI container by hand on startup and shows
/// the main window. Phase 2 wires up just enough — the backend and the orchestrator — to
/// prove the architecture composes; later phases register the engine, view models, etc.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddLogging();

        // Architecture seam: the App depends only on the Core abstraction; the concrete
        // SendInput-based backend lives in TypeGent.Native and is swapped in here.
        services.AddSingleton<IKeyboardBackend, InputSimulatorPlusBackend>();
        services.AddSingleton<TypingOrchestrator>();

        services.AddSingleton<MainWindow>();

        _services = services.BuildServiceProvider();

        var window = _services.GetRequiredService<MainWindow>();
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
