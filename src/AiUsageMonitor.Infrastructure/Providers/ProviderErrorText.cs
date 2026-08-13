using System.ComponentModel;
using System.Net.Http;
using System.Text.Json;

namespace AiUsageMonitor.Infrastructure.Providers;

/// <summary>
/// One app-authored line describing a probe failure, fit to render on a card. Never returns text
/// this application did not write: an arbitrary exception message can carry a path, a payload
/// fragment, or a header, and a card is always visible.
/// </summary>
public static class ProviderErrorText
{
    public static string For(Exception ex) => ex switch
    {
        ProviderMechanismException mechanism => mechanism.Message,
        HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError } =>
            "The usage endpoint could not be resolved. Check the network connection.",
        HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError } =>
            "The usage endpoint could not be reached.",
        HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError } =>
            "The secure connection to the usage endpoint failed.",
        HttpRequestException => "The request to the usage endpoint failed.",
        Win32Exception => "The provider executable could not be started.",
        IOException => "Communication with the provider executable failed.",
        JsonException => "The provider returned a response that could not be read.",
        _ => $"The provider probe failed unexpectedly ({ex.GetType().Name}).",
    };
}
