using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TypeGent.App.Settings;

/// <summary>
/// File-backed <see cref="ISettingsStore"/>: loads/saves <see cref="AppSettings"/> as JSON in
/// <c>%AppData%\TypeGent\settings.json</c>. Zero extra dependencies — <see cref="System.Text.Json"/>
/// is built into .NET 10. A missing or corrupt file is treated as "no settings yet" so the app
/// never crashes on a bad hand-edit or a first run.
/// </summary>
internal sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TypeGent",
        "settings.json");

    public AppSettings? Load()
    {
        try
        {
            var path = FilePath;
            if (!File.Exists(path)) return null;

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, Options);
        }
        catch
        {
            // Corrupt/unreadable file → fall back to defaults. Better than crashing on launch.
            return null;
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(settings, Options);
            File.WriteAllText(path, json);
        }
        catch
        {
            // Swallow write failures (read-only AppData, disk full, etc.) — settings aren't critical.
        }
    }
}