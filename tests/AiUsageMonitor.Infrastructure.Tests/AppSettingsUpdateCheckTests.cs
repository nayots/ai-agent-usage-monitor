using System.Text.Json;
using AiUsageMonitor.Infrastructure.Settings;

namespace AiUsageMonitor.Infrastructure.Tests;

public sealed class AppSettingsUpdateCheckTests
{
    [Fact]
    public void Update_checking_is_on_by_default()
    {
        // Spec D3. This overrides C18's recorded "off by default" recommendation deliberately;
        // if this assertion is ever flipped, the spec and the README must move with it.
        Assert.True(AppSettings.Default.UpdateCheckEnabled);
    }

    [Fact]
    public void Remembers_nothing_about_a_check_until_one_has_run()
    {
        Assert.Null(AppSettings.Default.LastUpdateCheckUtc);
        Assert.Null(AppSettings.Default.LastUpdateCheckETag);
        Assert.Null(AppSettings.Default.LastNotifiedUpdateVersion);
    }

    [Fact]
    public void Round_trips_through_the_settings_file()
    {
        AppSettings settings = AppSettings.Default with
        {
            UpdateCheckEnabled = false,
            LastUpdateCheckUtc = new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.Zero),
            LastUpdateCheckETag = "\"abc\"",
            LastNotifiedUpdateVersion = "0.1.4"
        };

        AppSettings? restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings));

        Assert.NotNull(restored);
        Assert.False(restored.UpdateCheckEnabled);
        Assert.Equal(new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.Zero), restored.LastUpdateCheckUtc);
        Assert.Equal("\"abc\"", restored.LastUpdateCheckETag);
        Assert.Equal("0.1.4", restored.LastNotifiedUpdateVersion);
    }
}
