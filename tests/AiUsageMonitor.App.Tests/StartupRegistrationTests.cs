using AiUsageMonitor.App.Interop;
using Microsoft.Win32;

namespace AiUsageMonitor.App.Tests;

/// <summary>
/// Exercises the real registry against a scratch key under HKCU rather than a mock of the API
/// under test. The key is deleted in Dispose whatever the test did.
/// </summary>
public sealed class StartupRegistrationTests : IDisposable
{
    private const string ScratchKey = @"Software\AiUsageMonitor\tests\Run";
    private const string ValueName = "AiUsageMonitorTest";

    private static StartupRegistration Registration(string? path) => new(ScratchKey, ValueName, path);

    [Fact]
    public void ANewMachineStartsDisabled() => Assert.False(Registration(@"C:\app\widget.exe").IsEnabled);

    [Fact]
    public void EnablingThenReadingReportsEnabled()
    {
        StartupRegistration registration = Registration(@"C:\app\widget.exe");

        registration.Enable();

        Assert.True(registration.IsEnabled);
    }

    [Fact]
    public void EnablingTwiceIsNotAnError()
    {
        StartupRegistration registration = Registration(@"C:\app\widget.exe");

        registration.Enable();
        registration.Enable();

        Assert.True(registration.IsEnabled);
    }

    [Fact]
    public void DisablingRemovesTheValueAndIsSafeWhenAbsent()
    {
        StartupRegistration registration = Registration(@"C:\app\widget.exe");
        registration.Enable();

        registration.Disable();
        registration.Disable();

        Assert.False(registration.IsEnabled);
    }

    [Fact]
    public void AnEntryPointingAtADifferentExecutableReadsAsDisabled()
    {
        // A moved or reinstalled app must show the checkbox off, so that turning it on rewrites the
        // entry to the new location. Reporting a third "registered elsewhere" state would be one
        // more thing the UI has to explain for no gain.
        Registration(@"C:\old\widget.exe").Enable();

        Assert.False(Registration(@"C:\new\widget.exe").IsEnabled);
    }

    [Fact]
    public void EnablingAfterAMoveOverwritesTheOldPath()
    {
        Registration(@"C:\old\widget.exe").Enable();
        StartupRegistration moved = Registration(@"C:\new\widget.exe");

        moved.Enable();

        Assert.True(moved.IsEnabled);
        Assert.False(Registration(@"C:\old\widget.exe").IsEnabled);
    }

    [Fact]
    public void WithoutAKnownExecutableTheFeatureReportsItselfUnsupported()
    {
        StartupRegistration registration = Registration(null);

        Assert.False(registration.IsSupported);
        Assert.False(registration.IsEnabled);
    }

    public void Dispose()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\AiUsageMonitor\tests", throwOnMissingSubKey: false);
        }
        catch (UnauthorizedAccessException)
        {
            // A locked key must not fail an otherwise passing test.
        }
    }
}
