using System.ComponentModel;
using System.Net.Http;
using System.Text.Json;
using AiUsageMonitor.Infrastructure.Providers;

namespace AiUsageMonitor.Infrastructure.Tests;

public class ProviderErrorTextTests
{
    [Theory]
    [InlineData(HttpRequestError.NameResolutionError, "The usage endpoint could not be resolved. Check the network connection.")]
    [InlineData(HttpRequestError.ConnectionError, "The usage endpoint could not be reached.")]
    [InlineData(HttpRequestError.SecureConnectionError, "The secure connection to the usage endpoint failed.")]
    [InlineData(HttpRequestError.Unknown, "The request to the usage endpoint failed.")]
    public void HttpFailuresHaveFixedSafeCopy(HttpRequestError error, string expected) =>
        Assert.Equal(expected, ProviderErrorText.For(new HttpRequestException(error, "token=sk-secret-abc123", null, null)));

    [Fact]
    public void KnownLocalFailuresHaveFixedSafeCopy()
    {
        Assert.Equal("Mechanism failed.", ProviderErrorText.For(new ProviderMechanismException("Mechanism failed.")));
        Assert.Equal("The provider executable could not be started.", ProviderErrorText.For(new Win32Exception("C:\\secret")));
        Assert.Equal("Communication with the provider executable failed.", ProviderErrorText.For(new IOException("C:\\secret")));
        Assert.Equal("The provider returned a response that could not be read.", ProviderErrorText.For(new JsonException("token=sk-secret-abc123")));
    }

    [Fact]
    public void FallbackAndHttpFailuresNeverIncludeTheExceptionMessage()
    {
        Assert.DoesNotContain("sk-secret-abc123", ProviderErrorText.For(new HttpRequestException("token=sk-secret-abc123")));
        Assert.DoesNotContain("token=", ProviderErrorText.For(new HttpRequestException("token=sk-secret-abc123")));
        Assert.Equal("The provider probe failed unexpectedly (InvalidOperationException).", ProviderErrorText.For(new InvalidOperationException("C:\\secret")));
    }
}
