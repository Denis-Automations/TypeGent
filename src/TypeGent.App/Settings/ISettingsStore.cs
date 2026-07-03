namespace TypeGent.App.Settings;

/// <summary>
/// Loads and saves user settings. Implemented by <see cref="JsonSettingsStore"/> (file-backed).
/// Registered in DI so the ViewModel can restore on startup and the App can persist on shutdown
/// without touching the file directly.
/// </summary>
public interface ISettingsStore
{
    /// <summary>Load settings, or return <c>null</c> when none are stored yet (first run).</summary>
    AppSettings? Load();

    /// <summary>Persist the given settings, replacing any previous file.</summary>
    void Save(AppSettings settings);
}