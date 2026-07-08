using BendIt.Api.Models;

namespace BendIt.Api.Tests.Support;

internal static class TestSupport
{
    public static void AssertContains(IEnumerable<string> lines, string expected)
    {
        if (!lines.Any(line => string.Equals(line.Trim(), expected, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"expected api-list.txt to contain '{expected}'");
        }
    }

    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("unable to locate repository root");
    }

    public static Endpoint EndpointFor(LocalApiServer server, string id, string path, string method = "GET")
    {
        var uri = new Uri(server.BaseUrl);
        return new Endpoint
        {
            Id = id,
            Method = method,
            Scheme = uri.Scheme,
            Host = uri.Host,
            Port = uri.Port,
            Path = path,
            Classification = "likely_exists",
            Confidence = 70
        };
    }
}
