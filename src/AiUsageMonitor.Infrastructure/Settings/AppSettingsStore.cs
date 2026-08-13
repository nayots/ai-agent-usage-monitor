using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace AiUsageMonitor.Infrastructure.Settings;

/// <summary>
/// Outcome of a load. <paramref name="CorruptBackupPath"/> is non-null only when an unreadable
/// settings file was moved aside; diagnostics surfaces it so a silent reset is never silent.
/// </summary>
public sealed record SettingsLoadResult(AppSettings Settings, string? CorruptBackupPath);

/// <summary>
/// Reads and writes <see cref="AppSettings"/> as JSON. Never throws on a damaged or missing
/// file: the application must always start, with defaults if necessary.
/// </summary>
public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        DefaultJsonTypeInfoResolver resolver = new();
        resolver.Modifiers.Add(typeInfo =>
        {
            if (typeInfo.Type == typeof(AppSettings))
            {
                JsonPropertyInfo providerOrder = typeInfo.Properties.Single(property => property.Name == nameof(AppSettings.ProviderOrder));
                providerOrder.ShouldSerialize = (_, value) => value is IReadOnlyCollection<string> { Count: > 0 };
            }
        });

        return new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = resolver,
            Converters =
            {
                new TolerantEnumConverter<ThemePreference>(ThemePreference.System),
                new TolerantEnumConverter<WidgetDensity>(WidgetDensity.Normal),
                new TolerantEnumConverter<MiniDock>(MiniDock.Top)
            }
        };
    }

    private readonly string _path;

    public AppSettingsStore(string path) => _path = path;

    /// <summary>
    /// %APPDATA%\AiUsageMonitor\settings.json, resolved for whichever user is running. Never a
    /// literal path: the release artifact has to run on a machine that is not the author's.
    /// </summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AiUsageMonitor",
        "settings.json");

    public SettingsLoadResult Load()
    {
        if (!File.Exists(_path))
        {
            return new SettingsLoadResult(AppSettings.Default, null);
        }

        try
        {
            string json = File.ReadAllText(_path);
            AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
            return settings is null
                ? QuarantineAndDefault()
                : new SettingsLoadResult(NormalizeProviderPreferences(settings), null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return QuarantineAndDefault();
        }
    }

    public void Save(AppSettings settings)
    {
        string directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("Settings path has no directory component.");
        Directory.CreateDirectory(directory);

        // Write-then-move so a crash mid-write cannot leave a truncated settings file behind.
        string temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, SerializerOptions));
        File.Move(temporary, _path, overwrite: true);
    }

    private SettingsLoadResult QuarantineAndDefault()
    {
        string backup = _path + "." + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + ".corrupt";

        try
        {
            File.Move(_path, backup, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The file could not be preserved. Starting with defaults still beats not starting.
            return new SettingsLoadResult(AppSettings.Default, null);
        }

        return new SettingsLoadResult(AppSettings.Default, backup);
    }

    private static AppSettings NormalizeProviderPreferences(AppSettings settings) => settings with
    {
        ProviderOrder = settings.ProviderOrder ?? [],
        HiddenProviders = settings.HiddenProviders ?? [],
        ProviderRefreshSeconds = settings.ProviderRefreshSeconds ?? new Dictionary<string, int>()
    };

    private sealed class TolerantEnumConverter<T>(T fallback) : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String &&
                Enum.TryParse(reader.GetString(), ignoreCase: false, out T value) &&
                Enum.IsDefined(value))
            {
                return value;
            }

            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int number))
            {
                T numericValue = (T)Enum.ToObject(typeof(T), number);
                if (Enum.IsDefined(numericValue))
                {
                    return numericValue;
                }
            }

            return fallback;
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
