using Microsoft.Extensions.Logging;

namespace AiUsageMonitor.Infrastructure.Settings;

/// <summary>
/// The single owner of application settings for the life of the process.
/// <para>
/// <see cref="Update"/> takes a function rather than a value on purpose: every caller edits the
/// state that exists now instead of one it captured earlier. A caller that held an
/// <see cref="AppSettings"/> from startup and wrote it back - which saving the window position
/// does on every shutdown - would otherwise revert every change made in between.
/// </para>
/// </summary>
public sealed class SettingsService
{
    private readonly AppSettingsStore _store;
    private readonly ILogger<SettingsService>? _logger;

    public SettingsService(AppSettingsStore store, AppSettings initial, ILogger<SettingsService>? logger = null)
    {
        _store = store;
        _logger = logger;
        Current = initial;
    }

    public AppSettings Current { get; private set; }

    public bool PersistenceFailed { get; private set; }

    public event EventHandler<AppSettings>? Changed;

    public event EventHandler? PersistenceStateChanged;

    /// <summary>
    /// Applies <paramref name="change"/> to <see cref="Current"/>, announces it, then persists.
    /// <para>
    /// In that order, and deliberately. A settings file that cannot be written is a bad reason to
    /// refuse a change the user has already watched take effect on screen; the change stands for
    /// this session and is lost at restart, which is the failure the user can actually understand.
    /// <see cref="AppSettings"/> is a record, so an update that produces an equal value is not a
    /// change and is not announced.
    /// </para>
    /// </summary>
    public void Update(Func<AppSettings, AppSettings> change)
    {
        AppSettings updated = change(Current);

        if (updated == Current)
        {
            return;
        }

        Current = updated;
        Changed?.Invoke(this, updated);
        Persist(updated);
    }

    /// <summary>
    /// Puts every application setting back to its default and returns where the previous settings
    /// were preserved, or null when there were none to preserve or they could not be copied
    /// (PRD §19). Provider configuration is not stored here and is not touched.
    /// <para>
    /// Two departures from <see cref="Update"/>, both deliberate. Pinning survives, because it is
    /// session state rather than a setting - see <see cref="AppSettings.AlwaysOnTop"/>. And the file
    /// is rewritten even when the loaded record already equals the defaults: reset means "make the
    /// file defaults", so state the record never carried - hand-edited keys, a value normalized on
    /// read - must not survive it.
    /// </para>
    /// <para>
    /// The startup registry entry is not reachable from here and is cleared by the caller. It is the
    /// one application setting that does not live in this file, and a reset that left it behind
    /// would be silently undone: the entry is read back from the registry on the next load.
    /// </para>
    /// </summary>
    public string? Reset()
    {
        string? backup = _store.BackUp();
        AppSettings defaults = AppSettings.Default with { AlwaysOnTop = Current.AlwaysOnTop };

        if (defaults != Current)
        {
            Current = defaults;
            Changed?.Invoke(this, defaults);
        }

        Persist(defaults);
        return backup;
    }

    private void Persist(AppSettings settings)
    {
        try
        {
            _store.Save(settings);
            SetPersistenceFailed(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetPersistenceFailed(true);
            _logger?.LogWarning(ex, "Settings could not be saved; the change applies to this session only.");
        }
    }

    private void SetPersistenceFailed(bool failed)
    {
        if (PersistenceFailed == failed)
        {
            return;
        }

        PersistenceFailed = failed;
        PersistenceStateChanged?.Invoke(this, EventArgs.Empty);
    }
}
