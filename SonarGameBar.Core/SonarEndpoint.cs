using System.Net;

namespace SonarGameBar.Core;

public static class SonarEndpoint
{
    public static Uri CreateLoopbackBaseUri(string? address, string defaultScheme)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new SonarApiException("SteelSeries GG did not provide a Sonar server address.");
        }

        var candidate = address.Contains("://", StringComparison.Ordinal)
            ? address
            : $"{defaultScheme}://{address}";

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            !IsLoopbackUri(uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new SonarApiException("SteelSeries GG provided an unsafe Sonar server address.");
        }

        return new UriBuilder(uri)
        {
            Path = uri.AbsolutePath.TrimEnd('/') + "/",
        }.Uri;
    }

    public static bool IsLoopbackUri(Uri uri)
    {
        if (uri.IsLoopback)
        {
            return true;
        }

        return IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address);
    }
}
