using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using AiUsageMonitor.Infrastructure.Logging;

namespace AiUsageMonitor.Infrastructure.Diagnostics;

public sealed record EnvironmentReport(
    string ApplicationVersion,
    string RuntimeVersion,
    string OperatingSystem,
    string LogDirectory,
    bool LogDirectoryWritable,
    bool IsElevated)
{
    public static EnvironmentReport Capture()
    {
        string logDirectory = SafeValue(() => RollingFileLoggerProvider.DefaultDirectory, "unknown");

        return new EnvironmentReport(
            ApplicationVersion: CaptureApplicationVersion(),
            RuntimeVersion: SafeValue(() => RuntimeInformation.FrameworkDescription, "unknown"),
            OperatingSystem: SafeValue(() => RuntimeInformation.OSDescription, "unknown"),
            LogDirectory: logDirectory,
            LogDirectoryWritable: IsDirectoryWritable(logDirectory),
            IsElevated: IsCurrentUserElevated());
    }

    /// <summary>
    /// The running build's version with the SDK's <c>+&lt;commit&gt;</c> build metadata stripped, or
    /// "unknown" when it cannot be read. Public because the widget footer states the same version the
    /// diagnostics bundle does, and two independent readings could disagree.
    /// </summary>
    public static string CaptureApplicationVersion()
    {
        try
        {
            Assembly? assembly = Assembly.GetEntryAssembly();
            string? informationalVersion = assembly?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            string? version = !string.IsNullOrWhiteSpace(informationalVersion)
                ? informationalVersion
                : assembly?.GetName().Version?.ToString();

            if (string.IsNullOrWhiteSpace(version))
            {
                return "unknown";
            }

            int metadataStart = version.IndexOf('+');
            string withoutMetadata = metadataStart >= 0 ? version[..metadataStart] : version;
            return string.IsNullOrWhiteSpace(withoutMetadata) ? "unknown" : withoutMetadata;
        }
        catch
        {
            return "unknown";
        }
    }

    private static bool IsDirectoryWritable(string directory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            Directory.CreateDirectory(directory);
            string probePath = Path.Combine(directory, $".diagnostics-{Guid.NewGuid():N}.tmp");
            using (File.Create(probePath))
            {
            }

            File.Delete(probePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsCurrentUserElevated()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            return new WindowsPrincipal(WindowsIdentity.GetCurrent())
                .IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static string SafeValue(Func<string> capture, string fallback)
    {
        try
        {
            string value = capture();
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }
}
