using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.Infrastructure.Updates;

namespace AiUsageMonitor.App.Tests;

public sealed class UpdateAnnouncementTests
{
    private static UpdateStatus Available(string latest) => new(
        UpdateAvailability.UpdateAvailable,
        ReleaseVersion.Parse("0.1.3"),
        ReleaseVersion.Parse(latest),
        DateTimeOffset.UnixEpoch,
        null);

    [Fact]
    public void Announces_a_version_that_has_not_been_announced()
        => Assert.True(UpdateAnnouncement.ShouldAnnounce(Available("0.1.4"), lastNotifiedVersion: null));

    [Fact]
    public void Never_announces_the_same_version_twice()
        => Assert.False(UpdateAnnouncement.ShouldAnnounce(Available("0.1.4"), "0.1.4"));

    [Fact]
    public void Announces_the_next_release_after_a_declined_one()
        => Assert.True(UpdateAnnouncement.ShouldAnnounce(Available("0.1.5"), "0.1.4"));

    [Fact]
    public void Does_not_announce_an_incomplete_available_status()
    {
        UpdateStatus status = new(
            UpdateAvailability.UpdateAvailable,
            ReleaseVersion.Parse("0.1.3"),
            null,
            DateTimeOffset.UnixEpoch,
            null);

        Assert.False(UpdateAnnouncement.ShouldAnnounce(status, null));
    }

    [Theory]
    [InlineData(UpdateAvailability.Current)]
    [InlineData(UpdateAvailability.Unknown)]
    public void Says_nothing_when_there_is_no_update(UpdateAvailability availability)
    {
        UpdateStatus status = new(availability, ReleaseVersion.Parse("0.1.3"), null, null, null);

        Assert.False(UpdateAnnouncement.ShouldAnnounce(status, null));
    }
}
