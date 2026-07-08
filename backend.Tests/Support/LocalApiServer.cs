using System.Net.Sockets;
using System.Text;

namespace BendIt.Api.Tests.Support;

internal sealed class LocalApiServer : IAsyncDisposable
{
    private readonly TcpListener listener;
    private readonly CancellationTokenSource stop = new();
    private readonly Dictionary<string, int> requestCounts = new(StringComparer.Ordinal);
    private readonly Task loop;

    private LocalApiServer(TcpListener listener, string baseUrl)
    {
        this.listener = listener;
        BaseUrl = baseUrl;
        loop = Task.Run(ServeAsync);
    }

    public string BaseUrl { get; }

    public static Task<LocalApiServer> StartAsync()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var baseUrl = $"http://127.0.0.1:{port}";
        return Task.FromResult(new LocalApiServer(listener, baseUrl));
    }

    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        listener.Stop();
        try
        {
            await loop;
        }
        catch (SocketException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            stop.Dispose();
        }
    }

    private async Task ServeAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            var client = await listener.AcceptTcpClientAsync(stop.Token);
            _ = Task.Run(() => RespondAsync(client), stop.Token);
        }
    }

    private async Task RespondAsync(TcpClient client)
    {
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        var requestLine = await reader.ReadLineAsync() ?? "";
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? headerLine;
        while (!string.IsNullOrEmpty(headerLine = await reader.ReadLineAsync()))
        {
            var separator = headerLine.IndexOf(':');
            if (separator > 0)
            {
                headers[headerLine[..separator].Trim()] = headerLine[(separator + 1)..].Trim();
            }
        }

        var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var method = parts.Length >= 1 ? parts[0] : "";
        var requestTarget = parts.Length >= 2 ? parts[1] : "/";
        var path = requestTarget.Split('?', 2)[0];
        var query = requestTarget.Contains('?') ? requestTarget.Split('?', 2)[1] : "";
        var requestBody = await ReadBodyAsync(reader, headers);
        var count = CountRequest(path);

        if (path == "/api/orgrow/grow/isalive")
        {
            if (!IsMethod(method, "GET"))
            {
                await WriteAsync(stream, 405, """{"error":"method not allowed"}""", "application/json");
                return;
            }

            await WriteAsync(stream, 200, "true", "text/plain");
            return;
        }

        if (path == "/api/profile")
        {
            await WriteAsync(stream, 200, """{"email":"alice@example.com","access_token":"secret-token-value"}""", "application/json");
            return;
        }

        if (path is "/api/users" or "/api/orders" or "/api/items" or "/api/health" or "/health" or "/api/v1/legacy/users")
        {
            await WriteAsync(stream, 200, """{"ok":true}""", "application/json");
            return;
        }

        if (path is "/api/orgrow/environment/setup/1/1" or "/api/orgrow/environment/setup/2/2" or "/api/orgrow/product/create")
        {
            await WriteAsync(stream, 200, """{"ok":true}""", "application/json");
            return;
        }

        if (path == "/api/method/strict")
        {
            await WriteAsync(
                stream,
                IsMethod(method, "GET") ? 200 : 405,
                IsMethod(method, "GET") ? """{"ok":true}""" : """{"error":"method not allowed"}""",
                "application/json");
            return;
        }

        if (path == "/api/method/open")
        {
            await WriteAsync(stream, 200, """{"ok":true,"methodAccepted":true}""", "application/json");
            return;
        }

        if (path == "/api/response-diff/stable")
        {
            await WriteAsync(stream, 200, """{"ok":true,"stable":true}""", "application/json");
            return;
        }

        if (path == "/api/response-diff/flaky")
        {
            var body = count % 2 == 0 ? """{"ok":true,"state":"changed"}""" : """{"ok":true,"state":"initial"}""";
            await WriteAsync(stream, 200, body, "application/json");
            return;
        }

        if (path == "/api/content-type/strict")
        {
            await WriteContentTypeResponseAsync(stream, method, headers, acceptTextPlain: false, acceptMissing: false);
            return;
        }

        if (path == "/api/content-type/text-plain-accepted")
        {
            await WriteContentTypeResponseAsync(stream, method, headers, acceptTextPlain: true, acceptMissing: false);
            return;
        }

        if (path == "/api/content-type/missing-accepted")
        {
            await WriteContentTypeResponseAsync(stream, method, headers, acceptTextPlain: false, acceptMissing: true);
            return;
        }

        if (path == "/api/error-disclosure/safe-validation")
        {
            await WriteAsync(stream, 400, """{"error":"validation failed","fields":{"email":"required"}}""", "application/json");
            return;
        }

        if (path == "/api/error-disclosure/internal-fields")
        {
            await WriteAsync(stream, 400, """{"error":"ModelState validation failed","details":{"tenantId":"The tenantId field is required","isAdmin":"Caller cannot set isAdmin"}}""", "application/json");
            return;
        }

        if (path == "/api/error-disclosure/stack-trace")
        {
            await WriteAsync(stream, 500, """{"error":"System.NullReferenceException: Object reference not set to an instance of an object.","stackTrace":" at BendIt.Api.Controllers.UsersController.Create(UserDto dto) in C:\\src\\BendIt\\Controllers\\UsersController.cs:line 42"}""", "application/json");
            return;
        }

        if (path == "/api/error-disclosure/database-timeout")
        {
            await WriteAsync(stream, 500, """{"error":"SqlException: timeout expired while acquiring a connection from the SQL Server connection pool for internal service CustomerDb"}""", "application/json");
            return;
        }

        if (path == "/api/cors/no-headers")
        {
            await WriteAsync(stream, 200, """{"ok":true}""", "application/json");
            return;
        }

        if (path == "/api/cors/allowlist")
        {
            var corsHeaders = new Dictionary<string, string>();
            if (Origin(headers).Equals("https://app.example", StringComparison.OrdinalIgnoreCase))
            {
                corsHeaders["Access-Control-Allow-Origin"] = "https://app.example";
                corsHeaders["Vary"] = "Origin";
            }

            await WriteAsync(stream, 200, """{"ok":true}""", "application/json", corsHeaders);
            return;
        }

        if (path == "/api/cors/wildcard")
        {
            await WriteAsync(stream, 200, """{"ok":true}""", "application/json", new Dictionary<string, string>
            {
                ["Access-Control-Allow-Origin"] = "*"
            });
            return;
        }

        if (path == "/api/cors/reflected-credentials")
        {
            await WriteAsync(stream, 200, """{"ok":true}""", "application/json", new Dictionary<string, string>
            {
                ["Access-Control-Allow-Origin"] = Origin(headers),
                ["Access-Control-Allow-Credentials"] = "true",
                ["Vary"] = "Origin"
            });
            return;
        }

        if (path == "/api/cors/unsafe-preflight")
        {
            if (IsMethod(method, "OPTIONS"))
            {
                await WriteAsync(stream, 204, "", "application/json", new Dictionary<string, string>
                {
                    ["Access-Control-Allow-Origin"] = Origin(headers),
                    ["Access-Control-Allow-Methods"] = "GET, POST, PUT, PATCH, DELETE",
                    ["Access-Control-Allow-Headers"] = "Authorization, Content-Type, X-API-Key",
                    ["Access-Control-Allow-Credentials"] = "true",
                    ["Vary"] = "Origin"
                });
                return;
            }

            await WriteAsync(stream, 200, """{"ok":true}""", "application/json");
            return;
        }

        if (path == "/api/parameter-pollution/query-strict")
        {
            if (HasDuplicateParameter(query, "id") || HasDuplicateParameter(query, "role"))
            {
                await WriteAsync(stream, 400, """{"error":"duplicate parameters are not allowed"}""", "application/json");
                return;
            }

            await WriteAsync(stream, 200, """{"selectedId":"1","role":"user"}""", "application/json");
            return;
        }

        if (path == "/api/parameter-pollution/query-vulnerable")
        {
            var selectedId = LastParameterValue(query, "id") ?? "1";
            var role = LastParameterValue(query, "role") ?? "user";
            await WriteAsync(stream, 200, $$"""{"selectedId":"{{selectedId}}","role":"{{role}}","winner":"lastValue"}""", "application/json");
            return;
        }

        if (path == "/api/parameter-pollution/body-strict")
        {
            if (!IsMethod(method, "POST"))
            {
                await WriteAsync(stream, 405, """{"error":"method not allowed"}""", "application/json");
                return;
            }

            if (HasDuplicateParameter(requestBody, "id") || HasDuplicateParameter(requestBody, "role") || HasDuplicateJsonKey(requestBody, "id") || HasDuplicateJsonKey(requestBody, "role"))
            {
                await WriteAsync(stream, 422, """{"error":"duplicate request fields are not allowed"}""", "application/json");
                return;
            }

            await WriteAsync(stream, 200, """{"selectedId":"1","role":"user"}""", "application/json");
            return;
        }

        if (path == "/api/parameter-pollution/body-vulnerable")
        {
            if (!IsMethod(method, "POST"))
            {
                await WriteAsync(stream, 405, """{"error":"method not allowed"}""", "application/json");
                return;
            }

            var selectedId = LastParameterValue(requestBody, "id") ?? LastJsonValue(requestBody, "id") ?? "1";
            var role = LastParameterValue(requestBody, "role") ?? LastJsonValue(requestBody, "role") ?? "user";
            await WriteAsync(stream, 200, $$"""{"selectedId":"{{selectedId}}","role":"{{role}}","winner":"lastValue"}""", "application/json");
            return;
        }

        if (path == "/api/large")
        {
            await WriteAsync(stream, 200, new string('A', 130 * 1024), "text/plain");
            return;
        }

        if (path == "/api/rate-limit/throttled")
        {
            if (count is >= 3 and <= 6)
            {
                await WriteAsync(stream, 429, """{"error":"too many requests"}""", "application/json", new Dictionary<string, string>
                {
                    ["Retry-After"] = "1",
                    ["RateLimit-Limit"] = "2",
                    ["RateLimit-Remaining"] = "0"
                });
                return;
            }

            await WriteAsync(stream, 200, """{"ok":true}""", "application/json");
            return;
        }

        if (path == "/api/rate-limit/open" || path == "/api/rate-limit/cap")
        {
            await WriteAsync(stream, 200, """{"ok":true}""", "application/json");
            return;
        }

        if (path == "/api/rate-limit/degraded")
        {
            var body = count >= 5 ? """{"ok":false,"mode":"degraded"}""" : """{"ok":true}""";
            await WriteAsync(stream, 200, body, "application/json");
            return;
        }

        await WriteAsync(stream, 404, """{"error":"not found"}""", "application/json");
    }

    private int CountRequest(string path)
    {
        lock (requestCounts)
        {
            requestCounts.TryGetValue(path, out var count);
            count++;
            requestCounts[path] = count;
            return count;
        }
    }

    private static async Task<string> ReadBodyAsync(StreamReader reader, Dictionary<string, string> headers)
    {
        if (!headers.TryGetValue("Content-Length", out var rawLength) || !int.TryParse(rawLength, out var length) || length <= 0)
        {
            return "";
        }

        var buffer = new char[length];
        var read = 0;
        while (read < length)
        {
            var current = await reader.ReadAsync(buffer, read, length - read);
            if (current == 0)
            {
                break;
            }

            read += current;
        }

        return new string(buffer, 0, read);
    }

    private static bool HasDuplicateParameter(string input, string name)
    {
        return ParameterValues(input, name).Count > 1;
    }

    private static string? LastParameterValue(string input, string name)
    {
        return ParameterValues(input, name).LastOrDefault();
    }

    private static List<string> ParameterValues(string input, string name)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return [];
        }

        var values = new List<string>();
        foreach (var pair in input.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                values.Add(parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "");
            }
        }

        return values;
    }

    private static bool HasDuplicateJsonKey(string input, string name)
    {
        return JsonValues(input, name).Count > 1;
    }

    private static string? LastJsonValue(string input, string name)
    {
        return JsonValues(input, name).LastOrDefault();
    }

    private static List<string> JsonValues(string input, string name)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return [];
        }

        var matches = System.Text.RegularExpressions.Regex.Matches(
            input,
            "\"" + System.Text.RegularExpressions.Regex.Escape(name) + "\"\\s*:\\s*\"([^\"]*)\"",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return matches.Select(match => match.Groups[1].Value).ToList();
    }

    private static string Origin(Dictionary<string, string> headers)
    {
        return headers.TryGetValue("Origin", out var origin) ? origin : "";
    }

    private static async Task WriteAsync(Stream stream, int status, string body, string contentType, Dictionary<string, string>? headers = null)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var reason = status switch
        {
            200 => "OK",
            204 => "No Content",
            400 => "Bad Request",
            405 => "Method Not Allowed",
            415 => "Unsupported Media Type",
            429 => "Too Many Requests",
            500 => "Internal Server Error",
            _ => "Not Found"
        };
        var extraHeaders = headers is null || headers.Count == 0
            ? ""
            : string.Concat(headers.Select(header => $"{header.Key}: {header.Value}\r\n"));
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}\r\n{extraHeaders}Content-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header);
        await stream.WriteAsync(bytes);
    }

    private static async Task WriteContentTypeResponseAsync(Stream stream, string method, Dictionary<string, string> headers, bool acceptTextPlain, bool acceptMissing)
    {
        if (!IsMethod(method, "POST"))
        {
            await WriteAsync(stream, 405, """{"error":"method not allowed"}""", "application/json");
            return;
        }

        headers.TryGetValue("Content-Type", out var contentType);
        var normalized = contentType?.Split(';', 2)[0].Trim();
        var accepted = string.Equals(normalized, "application/json", StringComparison.OrdinalIgnoreCase) ||
                       acceptTextPlain && string.Equals(normalized, "text/plain", StringComparison.OrdinalIgnoreCase) ||
                       acceptMissing && string.IsNullOrWhiteSpace(normalized);

        if (accepted)
        {
            await WriteAsync(stream, 200, """{"accepted":true}""", "application/json");
            return;
        }

        await WriteAsync(stream, 415, """{"error":"unsupported media type"}""", "application/json");
    }

    private static bool IsMethod(string actual, string expected)
    {
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }
}
