using AiUsageMonitor.Infrastructure.Diagnostics;

namespace AiUsageMonitor.Infrastructure.Tests;

public class EnvironmentReportTests
{
    [Fact]
    public void CaptureReturnsNonEmptyRuntimeFactsAndAPlainApplicationVersion()
    {
        EnvironmentReport report = EnvironmentReport.Capture();

        Assert.False(string.IsNullOrWhiteSpace(report.ApplicationVersion));
        Assert.False(string.IsNullOrWhiteSpace(report.RuntimeVersion));
        Assert.False(string.IsNullOrWhiteSpace(report.OperatingSystem));
        Assert.DoesNotContain('+', report.ApplicationVersion);
    }
}
