using AiUsageMonitor.Infrastructure.Diagnostics;

namespace AiUsageMonitor.Infrastructure.Tests;

public class DiagnosticRedactionTests
{
    [Fact]
    public void RedactMasksTheCurrentUserProfileWithEitherSeparatorStyle()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string alternateProfile = profile.Replace('\\', '/');

        string? redacted = DiagnosticRedaction.Redact($"native={profile}; alternate={alternateProfile}");

        Assert.NotNull(redacted);
        Assert.Contains("%USERPROFILE%", redacted);
        Assert.DoesNotContain(profile, redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(alternateProfile, redacted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RedactMasksTheCurrentUserNameWhenItIsLongEnough()
    {
        string input = $"user={Environment.UserName}";

        string? redacted = DiagnosticRedaction.Redact(input);

        Assert.NotNull(redacted);
        if (Environment.UserName.Length >= 3)
        {
            Assert.Contains("%USERNAME%", redacted);
            Assert.DoesNotContain(Environment.UserName, redacted, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Equal(input, redacted);
        }
    }

    [Fact]
    public void RedactLeavesUnrelatedTextByteIdenticalAndHandlesNullAndEmpty()
    {
        const string input = "No local path or account name is present.";

        Assert.Equal(input, DiagnosticRedaction.Redact(input));
        Assert.Null(DiagnosticRedaction.Redact(null));
        Assert.Equal(string.Empty, DiagnosticRedaction.Redact(string.Empty));
    }
}
