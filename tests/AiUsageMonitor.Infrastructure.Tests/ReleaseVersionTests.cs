using AiUsageMonitor.Infrastructure.Updates;

namespace AiUsageMonitor.Infrastructure.Tests;

public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("0.1.3", 0, 1, 3, 0)]
    [InlineData("v0.1.3", 0, 1, 3, 0)]
    [InlineData("V0.1.3", 0, 1, 3, 0)]
    [InlineData(" v0.1.3 ", 0, 1, 3, 0)]
    [InlineData("0.1", 0, 1, 0, 0)]
    [InlineData("1.2.3.4", 1, 2, 3, 4)]
    [InlineData("v0.2.0-rc1", 0, 2, 0, 0)]
    [InlineData("0.1.3+abc123", 0, 1, 3, 0)]
    public void Parses_the_shapes_a_release_tag_can_take(
        string text, int major, int minor, int patch, int revision)
    {
        ReleaseVersion? version = ReleaseVersion.Parse(text);

        Assert.NotNull(version);
        Assert.Equal(new ReleaseVersion(major, minor, patch, revision), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown")]
    [InlineData("v")]
    [InlineData("1")]
    [InlineData("1.2.3.4.5")]
    [InlineData("1.x.3")]
    [InlineData("-1.2.3")]
    [InlineData("latest")]
    public void Refuses_anything_it_cannot_read_as_a_version(string? text) =>
        Assert.Null(ReleaseVersion.Parse(text));

    [Fact]
    public void Compares_numerically_so_a_tenth_patch_beats_a_ninth()
    {
        ReleaseVersion older = ReleaseVersion.Parse("0.1.9")!;
        ReleaseVersion newer = ReleaseVersion.Parse("0.1.10")!;

        Assert.True(newer.CompareTo(older) > 0);
        Assert.True(older.CompareTo(newer) < 0);
    }

    [Fact]
    public void Orders_across_every_component()
    {
        Assert.True(ReleaseVersion.Parse("1.0.0")!.CompareTo(ReleaseVersion.Parse("0.9.9")!) > 0);
        Assert.True(ReleaseVersion.Parse("0.2.0")!.CompareTo(ReleaseVersion.Parse("0.1.99")!) > 0);
        Assert.True(ReleaseVersion.Parse("0.1.3.1")!.CompareTo(ReleaseVersion.Parse("0.1.3")!) > 0);
        Assert.Equal(0, ReleaseVersion.Parse("v0.1.3")!.CompareTo(ReleaseVersion.Parse("0.1.3")!));
    }

    [Fact]
    public void Compares_greater_than_null()
    {
        Assert.True(ReleaseVersion.Parse("0.1.0")!.CompareTo(null) > 0);
    }

    [Theory]
    [InlineData("v0.1.3", "0.1.3")]
    [InlineData("0.1", "0.1.0")]
    [InlineData("1.2.3.4", "1.2.3.4")]
    [InlineData("v0.2.0-rc1", "0.2.0")]
    public void Renders_from_its_own_numbers_never_from_the_input(string text, string expected) =>
        Assert.Equal(expected, ReleaseVersion.Parse(text)!.ToString());
}
