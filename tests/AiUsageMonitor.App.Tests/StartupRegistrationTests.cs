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

    private static string? Stored()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(ScratchKey);
        return key?.GetValue(ValueName) as string;
    }

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
    public void AnEntryPointingAtADifferentExecutableStillReadsAsEnabled()
    {
        Registration(@"C:\old\widget.exe").Enable();

        StartupRegistration upgraded = Registration(@"C:\new\widget.exe");

        Assert.True(upgraded.IsEnabled);
        Assert.True(upgraded.IsRegisteredElsewhere);
    }

    [Fact]
    public void EnablingAfterAMoveOverwritesTheOldPath()
    {
        Registration(@"C:\old\widget.exe").Enable();
        StartupRegistration moved = Registration(@"C:\new\widget.exe");

        moved.Enable();

        Assert.True(moved.IsEnabled);
        Assert.False(moved.IsRegisteredElsewhere);
        Assert.Equal(@"""C:\new\widget.exe""", Stored());
    }

    [Fact]
    public void WithoutAKnownExecutableTheFeatureReportsItselfUnsupported()
    {
        StartupRegistration registration = Registration(null);

        Assert.False(registration.IsSupported);
        Assert.False(registration.IsEnabled);
    }

    [Fact]
    public void SyncPathRepointsAnEntryLeftByAnEarlierVersion()
    {
        Registration(@"C:\downloads\widget-v1.exe").Enable();
        StartupRegistration upgraded = Registration(@"C:\downloads\widget-v2.exe");

        upgraded.SyncPath();

        Assert.Equal(@"""C:\downloads\widget-v2.exe""", Stored());
        Assert.False(upgraded.IsRegisteredElsewhere);
    }

    [Fact]
    public void SyncPathNeverRegistersAnApplicationThatWasNotAlreadyRegistered()
    {
        // Repairing a registration the user asked for is the feature. Creating one they never
        // asked for would turn a startup housekeeping call into a setting change behind their back.
        StartupRegistration registration = Registration(@"C:\app\widget.exe");

        registration.SyncPath();

        Assert.False(registration.IsEnabled);
        Assert.Null(Stored());
    }

    [Fact]
    public void SyncPathLeavesAMatchingEntryUntouched()
    {
        StartupRegistration registration = Registration(@"C:\app\widget.exe");
        registration.Enable();

        registration.SyncPath();

        Assert.Equal(@"""C:\app\widget.exe""", Stored());
    }

    [Fact]
    public void SyncPathDoesNothingWithoutAKnownExecutable()
    {
        Registration(@"C:\old\widget.exe").Enable();

        Registration(null).SyncPath();

        Assert.Equal(@"""C:\old\widget.exe""", Stored());
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
