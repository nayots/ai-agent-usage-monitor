namespace AiUsageMonitor.Domain;

/// <summary>
/// A provider's own instruction to stop asking for a while. Provider-neutral by construction:
/// nothing here names a provider, a transport, a status code, or a header, so the scheduler can act
/// on it without learning any provider's semantics (PRD §21).
/// <para>
/// The presence of this value on a <see cref="ProviderSnapshot"/> means "this attempt was refused
/// because the caller is asking too often". <see cref="NotBefore"/> carries the provider's explicit
/// instant when it named one, and is null when the provider refused without saying for how long —
/// which is the scheduler's cue to apply its own fallback rather than invent an instant here.
/// </para>
/// <para>
/// Deliberately NOT called a rate limit. In this application a rate limit is a quota window — the
/// thing the widget displays — and Codex's own mechanism is literally
/// <c>account/rateLimits/read</c>. Overloading the term would give two unrelated concepts one name.
/// </para>
/// </summary>
public sealed record ThrottleAdvice(DateTimeOffset? NotBefore)
{
    /// <summary>
    /// Whether the provider named the instant itself. The scheduler honours a provider-specified
    /// instant exactly and never shortens it; an application-authored wait is used only when this
    /// is false.
    /// </summary>
    public bool IsProviderSpecified => NotBefore is not null;
}
