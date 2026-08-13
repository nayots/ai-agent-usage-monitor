using System.Text.Json;
using AiUsageMonitor.Infrastructure.Providers;
using AiUsageMonitor.Infrastructure.Providers.Codex;

namespace AiUsageMonitor.Infrastructure.Tests;

public class CodexProtocolTests
{
    [Fact]
    public void NotificationsAreSkippedAndRecordedByMethodNameOnly()
    {
        List<string> notes = [];

        bool found = CodexProtocol.TryReadResult("""{"method":"remoteControl/status/changed","params":{"secret":"nope"}}""", notes, out _);

        Assert.False(found);
        Assert.Equal(["Observed and skipped unsolicited notification: remoteControl/status/changed"], notes);
    }

    [Theory]
    [InlineData("{\"id\":1,\"result\":{}}")]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[]")]
    [InlineData("{\"id\":\"2\",\"result\":{}}")]
    public void NonResultFramesAreIgnored(string line)
    {
        List<string> notes = [];

        bool found = CodexProtocol.TryReadResult(line, notes, out _);

        Assert.False(found);
        Assert.Empty(notes);
    }

    [Fact]
    public void AnIdTwoResultWithoutJsonRpcIsClonedForTheCaller()
    {
        List<string> notes = [];

        bool found = CodexProtocol.TryReadResult("""{"id":2,"result":{"rateLimits":[]}}""", notes, out JsonElement result);

        Assert.True(found);
        Assert.Equal(JsonValueKind.Array, result.GetProperty("rateLimits").ValueKind);
        Assert.Empty(notes);
    }

    [Fact]
    public void AnIdTwoErrorExposesOnlyTheAppAuthoredCodeAndSafeKeys()
    {
        List<string> notes = [];

        ProviderMechanismException ex = Assert.Throws<ProviderMechanismException>(() =>
            CodexProtocol.TryReadResult("""{"id":2,"error":{"code":-32600,"message":"Not initialized"}}""", notes, out _));

        Assert.Contains("-32600", ex.Message);
        Assert.DoesNotContain("{", ex.Message);
        Assert.DoesNotContain("Not initialized", ex.Message);
        Assert.Equal(["Codex error keys: code, message."], notes);
    }
}
