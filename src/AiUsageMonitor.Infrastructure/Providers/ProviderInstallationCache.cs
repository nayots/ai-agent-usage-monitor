namespace AiUsageMonitor.Infrastructure.Providers;

/// <summary>
/// What a probe found when it last looked for its provider on this machine: where the executable is,
/// and what version it reported. Both may be null - a null <see cref="ExecutablePath"/> is the
/// positive finding "this provider is not installed here", not an absence of information.
/// </summary>
public sealed record ProviderInstallation(string? ExecutablePath, string? Version);

/// <summary>
/// Holds a <see cref="ProviderInstallation"/> for a fixed lifetime so a widget polling every couple
/// of minutes does not re-walk PATH on every read. Installing, upgrading or removing a CLI happens a
/// few times a year, not a few times an hour.
/// <para>
/// This sits in front of <see cref="ProviderVersionCache"/> rather than replacing it, and the two
/// answer different questions. This one decides whether to look at the machine at all; that one,
/// keyed on path and last-write time, decides whether looking has to spawn <c>--version</c>. Both
/// are needed: without this, every poll re-walks PATH; without that, every expiry re-launches the
/// executable even when the binary has not changed since it was last read.
/// </para>
/// <para>
/// The cost of the lifetime is that a machine change goes unnoticed for up to <see cref="Lifetime"/>.
/// That is why "Re-check providers" exists in the settings window, and why it calls
/// <see cref="Invalidate"/> - a user who has just installed something has a way to say so rather
/// than waiting. The ordinary Refresh actions deliberately do not: they mean "get me current
/// numbers", which is a different request from "look at this machine again".
/// </para>
/// </summary>
public sealed class ProviderInstallationCache
{
    /// <summary>
    /// How long a detection is trusted. Thirty minutes is a compromise between not re-walking PATH
    /// for a fact that rarely changes and not leaving a freshly installed provider invisible for so
    /// long that the widget looks broken.
    /// </summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(30);

    private readonly object _gate = new();
    private ProviderInstallation? _installation;
    private DateTimeOffset _checkedAt;

    public ProviderInstallationCache(TimeSpan? lifetime = null) => Lifetime = lifetime ?? DefaultLifetime;

    /// <summary>This instance's lifetime, so a caller can say in its diagnostics what it is.</summary>
    public TimeSpan Lifetime { get; }

    /// <summary>
    /// The last detection when it is still within its lifetime, plus how long ago it was taken so the
    /// caller can say as much in its diagnostics.
    /// </summary>
    public bool TryGet(DateTimeOffset now, out ProviderInstallation installation, out TimeSpan age)
    {
        lock (_gate)
        {
            age = now - _checkedAt;

            // A negative age means the clock moved backwards - a manual change, a DST transition, or
            // a resume. Treat it as expired rather than trusting an entry that appears to come from
            // the future, which would otherwise pin the cache until the clock caught up.
            if (_installation is not null && age >= TimeSpan.Zero && age < Lifetime)
            {
                installation = _installation;
                return true;
            }
        }

        installation = new ProviderInstallation(null, null);
        age = TimeSpan.Zero;
        return false;
    }

    public void Store(ProviderInstallation installation, DateTimeOffset now)
    {
        lock (_gate)
        {
            _installation = installation;
            _checkedAt = now;
        }
    }

    /// <summary>Forgets the last detection, so the next probe looks at the machine again.</summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            _installation = null;
            _checkedAt = default;
        }
    }
}
