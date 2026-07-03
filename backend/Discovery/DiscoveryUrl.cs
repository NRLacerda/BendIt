namespace BendIt.Api.Discovery;

public sealed partial class DiscoveryOrchestrator
{
    private static bool IsHttpMethod(string value)
    {
        return value.Trim().ToUpperInvariant() is "GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS";
    }

    private static bool NormalizeEndpoint(string method, string rawUrl, out NormalizedEndpoint normalized)
    {
        normalized = new NormalizedEndpoint("", "", "", "", null, "", []);
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query).AllKeys.Where(key => key is not null).Cast<string>().Order(StringComparer.Ordinal).ToList();
        var path = NormalizePathTemplate(uri.AbsolutePath);
        method = method.Trim().ToUpperInvariant();
        int? port = uri.IsDefaultPort ? null : uri.Port;
        var authority = uri.Host.ToLowerInvariant() + (port is null ? "" : ":" + port.Value);
        var hashInput = method + " " + uri.Scheme.ToLowerInvariant() + "://" + authority + path;
        normalized = new NormalizedEndpoint(
            "endpoint_" + ShortHash(hashInput),
            method,
            uri.Scheme.ToLowerInvariant(),
            uri.Host.ToLowerInvariant(),
            port,
            path,
            queryParams);
        return true;
    }

    private static string NormalizePathTemplate(string path)
    {
        path = path.Trim();
        if (string.IsNullOrEmpty(path))
        {
            return "/";
        }

        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        while (path.Contains("//", StringComparison.Ordinal))
        {
            path = path.Replace("//", "/", StringComparison.Ordinal);
        }

        if (path.Length > 1)
        {
            path = path.TrimEnd('/');
        }

        var parts = path.Split('/');
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].All(char.IsDigit) && parts[i] != "")
            {
                parts[i] = "{id}";
            }
            else if (LooksLikeHexId(parts[i]))
            {
                parts[i] = "{value}";
            }
        }

        return string.Join('/', parts);
    }

    private static bool ResolveCandidateUrl(string baseUrl, string path, out string fullUrl)
    {
        fullUrl = "";
        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute))
        {
            fullUrl = absolute.ToString();
            return true;
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) || !Uri.TryCreate(baseUri, path, out var resolved))
        {
            return false;
        }

        fullUrl = resolved.ToString();
        return true;
    }

    private static bool TryDowngradeLocalHttpsUrl(string rawUrl, out string downgradedUrl)
    {
        downgradedUrl = "";
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !IsLocalHost(uri.Host))
        {
            return false;
        }

        var builder = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttp };
        if (uri.IsDefaultPort)
        {
            builder.Port = -1;
        }

        downgradedUrl = builder.Uri.ToString();
        return true;
    }

    private static IEnumerable<string> LocalHttpFallbackUrls(string rawUrl)
    {
        yield return rawUrl;
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        var builder = new UriBuilder(uri) { Host = "127.0.0.1" };
        yield return builder.Uri.ToString();
    }

    private static bool IsLocalHost(string host)
    {
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeHexId(string value)
    {
        return value.Length >= 8 && value.All(c => char.IsAsciiHexDigit(c) || c == '-');
    }
}
