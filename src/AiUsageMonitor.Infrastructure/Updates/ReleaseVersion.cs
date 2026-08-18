using System.Globalization;

namespace AiUsageMonitor.Infrastructure.Updates;

/// <summary>
/// A release version, parsed into numbers so it can be compared as one (PRD §28 update discovery;
/// spec D5). Comparing tags as strings reports 0.1.10 as older than 0.1.9, a defect that only
/// appears after ten patch releases and then looks like the feature is simply broken.
/// <para>
/// <see cref="Parse"/> returns null rather than throwing or guessing. A version this application
/// cannot read must surface as an explicit unknown, never as a default - the same rule the provider
/// adapters follow, where missing data is null and never zero.
/// </para>
/// <para>
/// <see cref="ToString"/> renders from these numbers and never echoes the input. The tag it was
/// parsed from is third-party text, and this repository has already shipped one defect of that
/// shape; re-rendering means the worst a hostile or corrupt reply can do is fail to parse.
/// </para>
/// </summary>
public sealed record ReleaseVersion(int Major, int Minor, int Patch, int Revision)
    : IComparable<ReleaseVersion>
{
    /// <summary>
    /// Reads <c>v0.1.3</c>, <c>0.1.3</c>, <c>0.1</c>, <c>1.2.3.4</c> and <c>v0.2.0-rc1</c>, and
    /// returns null for everything else - including the literal "unknown" that
    /// <c>EnvironmentReport.CaptureApplicationVersion</c> yields when it cannot read the assembly.
    /// <para>
    /// The prerelease and build-metadata suffixes are discarded rather than ordered. GitHub's
    /// <c>releases/latest</c> already excludes drafts and prereleases, so this exists only so that a
    /// mis-marked tag degrades to a sane comparison instead of to a parse failure.
    /// </para>
    /// </summary>
    public static ReleaseVersion? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string trimmed = text.Trim();

        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        int suffix = trimmed.IndexOfAny(['-', '+']);

        if (suffix >= 0)
        {
            trimmed = trimmed[..suffix];
        }

        string[] parts = trimmed.Split('.');

        if (parts.Length is < 2 or > 4)
        {
            return null;
        }

        int[] numbers = new int[4];

        for (int i = 0; i < parts.Length; i++)
        {
            // NumberStyles.None rejects a sign, whitespace and thousands separators, so "-1" and
            // " 2" fail here rather than parsing into something that compares plausibly.
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            {
                return null;
            }

            numbers[i] = value;
        }

        return new ReleaseVersion(numbers[0], numbers[1], numbers[2], numbers[3]);
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        int result = Major.CompareTo(other.Major);

        if (result != 0)
        {
            return result;
        }

        result = Minor.CompareTo(other.Minor);

        if (result != 0)
        {
            return result;
        }

        result = Patch.CompareTo(other.Patch);

        return result != 0 ? result : Revision.CompareTo(other.Revision);
    }

    /// <summary>
    /// Three components unless a fourth was given. A tag of "0.1" renders as "0.1.0" on purpose:
    /// this is the normalized value, not a copy of what arrived over the network.
    /// </summary>
    public override string ToString() => Revision > 0
        ? $"{Major}.{Minor}.{Patch}.{Revision}"
        : $"{Major}.{Minor}.{Patch}";
}
