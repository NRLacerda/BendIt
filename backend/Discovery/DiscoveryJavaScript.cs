using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using BendIt.Api.Models;

namespace BendIt.Api.Discovery;

public sealed partial class DiscoveryOrchestrator
{
    private const int MaxJavaScriptAssets = 32;
    private const long MaxJavaScriptBytes = 5 * 1024 * 1024;

    private static async Task<List<EndpointCandidate>> JavaScriptCandidatesAsync(
        string baseUrl,
        HttpClient client,
        DiscoveryRequest request,
        CancellationToken cancellationToken)
    {
        var entry = await ExecuteDiscoveryRequestAsync(client, HttpMethod.Get.Method, baseUrl, request.MaxBodyBytes, cancellationToken);
        if (entry.Error is not null || entry.Body.Length == 0)
        {
            return [];
        }

        var html = DecodeText(entry.Body);
        var assetUrls = CollectJavaScriptAssetUrls(baseUrl, html).Take(MaxJavaScriptAssets).ToList();
        var candidates = ExtractJavaScriptEndpointCandidates(baseUrl, html, baseUrl);

        await Parallel.ForEachAsync(assetUrls, new ParallelOptions { MaxDegreeOfParallelism = request.MaxWorkers, CancellationToken = cancellationToken }, async (assetUrl, token) =>
        {
            var asset = await ExecuteDiscoveryRequestAsync(client, HttpMethod.Get.Method, assetUrl, Math.Min(request.MaxBodyBytes, MaxJavaScriptBytes), token);
            if (asset.Error is not null || asset.Body.Length == 0)
            {
                return;
            }

            lock (candidates)
            {
                candidates.AddRange(ExtractJavaScriptEndpointCandidates(baseUrl, DecodeText(asset.Body), assetUrl));
            }
        });

        return candidates
            .GroupBy(candidate => candidate.Method + " " + candidate.Url, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static IEnumerable<string> CollectJavaScriptAssetUrls(string baseUrl, string html)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in ScriptSource().Matches(html).Cast<Match>().Concat(PreloadScriptHref().Matches(html).Cast<Match>()))
        {
            var value = WebUtility.HtmlDecode(match.Groups["url"].Value);
            if (ResolveCandidateUrl(baseUrl, value, out var fullUrl) && SameOrigin(baseUrl, fullUrl))
            {
                urls.Add(fullUrl);
            }
        }

        foreach (Match match in DynamicImport().Matches(html))
        {
            var value = WebUtility.HtmlDecode(match.Groups["url"].Value);
            if (ResolveCandidateUrl(baseUrl, value, out var fullUrl) && SameOrigin(baseUrl, fullUrl))
            {
                urls.Add(fullUrl);
            }
        }

        return urls;
    }

    private static List<EndpointCandidate> ExtractJavaScriptEndpointCandidates(string baseUrl, string source, string sourceDetail)
    {
        var candidates = new List<EndpointCandidate>();
        foreach (Match match in FetchCall().Matches(source))
        {
            AddJavaScriptCandidate(candidates, baseUrl, sourceDetail, match.Groups["url"].Value, MethodFromContext(match.Value), 95);
        }

        foreach (Match match in AxiosCall().Matches(source))
        {
            AddJavaScriptCandidate(candidates, baseUrl, sourceDetail, match.Groups["url"].Value, MethodFromContext(match.Value), 90);
        }

        foreach (Match match in ApiPathString().Matches(source))
        {
            AddJavaScriptCandidate(candidates, baseUrl, sourceDetail, match.Groups["url"].Value, HttpMethod.Get.Method, 60);
        }

        return candidates;
    }

    private static void AddJavaScriptCandidate(List<EndpointCandidate> candidates, string baseUrl, string sourceDetail, string rawPath, string method, int confidence)
    {
        var path = NormalizeJavaScriptPath(rawPath);
        if (string.IsNullOrEmpty(path) || IsThirdPartyUrl(baseUrl, path) || !ResolveCandidateUrl(baseUrl, path, out var fullUrl))
        {
            return;
        }

        candidates.Add(new EndpointCandidate(method, fullUrl, ["javascript"], sourceDetail, confidence, false));
    }

    private static string NormalizeJavaScriptPath(string value)
    {
        value = WebUtility.HtmlDecode(value.Trim().Trim('\'', '"', '`'));
        value = TemplateVariable().Replace(value, "123");
        value = value.Replace("\\/", "/", StringComparison.Ordinal);
        return value;
    }

    private static string MethodFromContext(string value)
    {
        var axiosMethodMatch = AxiosMethodCall().Match(value);
        if (axiosMethodMatch.Success)
        {
            return axiosMethodMatch.Groups["method"].Value.ToUpperInvariant();
        }

        var methodMatch = HttpMethodProperty().Match(value);
        return methodMatch.Success ? methodMatch.Groups["method"].Value.ToUpperInvariant() : HttpMethod.Get.Method;
    }

    private static bool IsThirdPartyUrl(string baseUrl, string rawUrl)
    {
        return Uri.TryCreate(rawUrl, UriKind.Absolute, out var absolute) && !SameOrigin(baseUrl, absolute.ToString());
    }

    private static bool SameOrigin(string left, string right)
    {
        return Uri.TryCreate(left, UriKind.Absolute, out var leftUri) &&
               Uri.TryCreate(right, UriKind.Absolute, out var rightUri) &&
               string.Equals(leftUri.Scheme, rightUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(leftUri.Authority, rightUri.Authority, StringComparison.OrdinalIgnoreCase);
    }

    private static string DecodeText(byte[] body)
    {
        return Encoding.UTF8.GetString(body);
    }

    [GeneratedRegex("""<script\b[^>]*\bsrc\s*=\s*["'](?<url>[^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptSource();

    [GeneratedRegex("""<link\b(?=[^>]*\brel\s*=\s*["'](?:preload|modulepreload)["'])(?=[^>]*\bhref\s*=\s*["'](?<url>[^"']+)["'])[^>]*>""", RegexOptions.IgnoreCase)]
    private static partial Regex PreloadScriptHref();

    [GeneratedRegex("""import\s*\(\s*["'`](?<url>[^"'`]+)["'`]\s*\)""", RegexOptions.IgnoreCase)]
    private static partial Regex DynamicImport();

    [GeneratedRegex("""fetch\s*\(\s*["'`](?<url>(?:https?://[^"'`]+|/[^"'`]+))["'`][\s\S]{0,250}?\)""", RegexOptions.IgnoreCase)]
    private static partial Regex FetchCall();

    [GeneratedRegex("""axios(?:\.\w+)?\s*\(\s*["'`](?<url>(?:https?://[^"'`]+|/[^"'`]+))["'`][\s\S]{0,250}?\)""", RegexOptions.IgnoreCase)]
    private static partial Regex AxiosCall();

    [GeneratedRegex("""["'`](?<url>/api/[A-Za-z0-9_./{}:$-]+)["'`]""", RegexOptions.IgnoreCase)]
    private static partial Regex ApiPathString();

    [GeneratedRegex("""method\s*:\s*["'`](?<method>GET|POST|PUT|PATCH|DELETE|HEAD|OPTIONS)["'`]""", RegexOptions.IgnoreCase)]
    private static partial Regex HttpMethodProperty();

    [GeneratedRegex("""axios\.(?<method>get|post|put|patch|delete|head|options)\s*\(""", RegexOptions.IgnoreCase)]
    private static partial Regex AxiosMethodCall();

    [GeneratedRegex(@"\$\{[^}]+\}|\{[^}/]+\}")]
    private static partial Regex TemplateVariable();
}
