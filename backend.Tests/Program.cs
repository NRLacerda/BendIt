using System.Net.Sockets;
using System.Text;
using BendIt.Api.Discovery;
using BendIt.Api.Models;
using BendIt.Api.TestBattery;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

var tests = new List<(string Name, Func<Task> Run)>
{
    ("native api list contains api routes from go implementation", NativeApiListContainsApiRoutes),
    ("discovery and battery produce results for local api", DiscoveryAndBatteryProduceResultsForLocalApi),
    ("likely exists health endpoint still produces result evidence", LikelyExistsHealthEndpointProducesResultEvidence),
    ("localhost https discovery falls back to http", LocalhostHttpsDiscoveryFallsBackToHttp),
    ("test battery caps response body capture", TestBatteryCapsResponseBodyCapture),
    ("test battery routes body tests by http verb", TestBatteryRoutesBodyTestsByHttpVerb)
};

foreach (var test in tests)
{
    await test.Run();
    Console.WriteLine("PASS " + test.Name);
}

static Task NativeApiListContainsApiRoutes()
{
    var lines = File.ReadAllLines(Path.Combine(RepoRoot(), "backend", "Resources", "api-list.txt"));
    AssertContains(lines, "GET /api/users");
    AssertContains(lines, "GET /api/orders/{id}");
    AssertContains(lines, "POST /api/token");
    return Task.CompletedTask;
}

static async Task DiscoveryAndBatteryProduceResultsForLocalApi()
{
    await using var server = await LocalApiServer.StartAsync();
    var discovery = new DiscoveryOrchestrator(new TestEnvironment(Path.Combine(RepoRoot(), "backend")));

    var project = new Project
    {
        ProjectId = "local-api",
        BaseUrl = server.BaseUrl,
        Auth = new AuthConfig { Type = "none" }
    };

    var endpoints = await discovery.RunAsync(project, new DiscoveryRequest
    {
        UseNativeApiList = true,
        UseSpecDiscovery = false,
        MaxWorkers = 4,
        TimeoutSeconds = 2
    }, [], CancellationToken.None);

    if (endpoints.Endpoints.Count == 0)
    {
        throw new InvalidOperationException("expected native API-list discovery to register endpoints");
    }

    var testable = DiscoveryOrchestrator.FilterTestableEndpoints(endpoints.Endpoints);
    if (testable.Count == 0)
    {
        throw new InvalidOperationException("expected discovered local API endpoints to be testable");
    }

    var results = await new TestBatteryRunner().RunAsync(project, testable, new TestRunRequest
    {
        BendTypes = ["authConsistency", "requestSize"],
        ParallelWorkers = 2
    }, CancellationToken.None);

    if (results.Results.Count == 0)
    {
        throw new InvalidOperationException("expected test battery to produce results for discovered endpoints");
    }
}

static async Task LikelyExistsHealthEndpointProducesResultEvidence()
{
    await using var server = await LocalApiServer.StartAsync();
    var discovery = new DiscoveryOrchestrator(new TestEnvironment(Path.Combine(RepoRoot(), "backend")));

    var project = new Project
    {
        ProjectId = "orgrow",
        BaseUrl = server.BaseUrl,
        Auth = new AuthConfig { Type = "none" }
    };

    var endpoints = await discovery.RunAsync(project, new DiscoveryRequest
    {
        UseNativeApiList = false,
        UseSpecDiscovery = false,
        ApiList = ["GET /api/orgrow/grow/isalive"],
        MaxWorkers = 2,
        TimeoutSeconds = 2
    }, [], CancellationToken.None);

    var endpoint = endpoints.Endpoints.SingleOrDefault(item => item.Path == "/api/orgrow/grow/isalive")
        ?? throw new InvalidOperationException("expected isalive endpoint to be discovered");

    if (endpoint.Classification != "likely_exists")
    {
        throw new InvalidOperationException($"expected text/plain true endpoint to be likely_exists, got {endpoint.Classification}");
    }

    var testable = DiscoveryOrchestrator.FilterTestableEndpoints(endpoints.Endpoints);
    if (testable.All(item => item.Id != endpoint.Id))
    {
        throw new InvalidOperationException("expected likely_exists isalive endpoint to be testable");
    }

    var results = await new TestBatteryRunner().RunAsync(project, testable, new TestRunRequest
    {
        BendTypes = ["authConsistency", "idMutation", "httpMethodValidation"],
        ParallelWorkers = 1
    }, CancellationToken.None);

    if (results.Results.Count == 0)
    {
        throw new InvalidOperationException("expected likely_exists isalive endpoint to produce raw evidence");
    }

    if (results.Results.All(result => result.ResultBody != "true"))
    {
        throw new InvalidOperationException("expected resultBody to contain the actual API response body");
    }

    if (results.Results.Any(result => result.Interesting))
    {
        throw new InvalidOperationException("expected public isalive endpoint evidence to be healthy, not a finding");
    }
}

static async Task LocalhostHttpsDiscoveryFallsBackToHttp()
{
    await using var server = await LocalApiServer.StartAsync();
    var discovery = new DiscoveryOrchestrator(new TestEnvironment(Path.Combine(RepoRoot(), "backend")));
    var httpsBaseUrl = server.BaseUrl
        .Replace("http://127.0.0.1:", "https://localhost:", StringComparison.Ordinal);

    var project = new Project
    {
        ProjectId = "localhost-https-fallback",
        BaseUrl = httpsBaseUrl,
        Auth = new AuthConfig { Type = "none" }
    };

    var endpoints = await discovery.RunAsync(project, new DiscoveryRequest
    {
        UseNativeApiList = false,
        UseSpecDiscovery = false,
        ApiList = ["GET /api/orgrow/grow/isalive"],
        MaxWorkers = 1,
        TimeoutSeconds = 2
    }, [], CancellationToken.None);

    var endpoint = endpoints.Endpoints.SingleOrDefault(item => item.Path == "/api/orgrow/grow/isalive")
        ?? throw new InvalidOperationException("expected isalive endpoint to be discovered through http fallback");

    if (endpoint.StatusCode != 200)
    {
        throw new InvalidOperationException($"expected localhost fallback endpoint status 200, got {endpoint.StatusCode}");
    }

    if (endpoint.Scheme != "http")
    {
        throw new InvalidOperationException($"expected fallback endpoint scheme http, got {endpoint.Scheme}");
    }

    if (endpoint.Port is null)
    {
        throw new InvalidOperationException("expected fallback endpoint to preserve local port");
    }

    if (endpoint.Classification != "likely_exists" || endpoint.Confidence < 60)
    {
        throw new InvalidOperationException($"expected likely_exists with confidence >= 60, got {endpoint.Classification} {endpoint.Confidence}");
    }
}

static async Task TestBatteryCapsResponseBodyCapture()
{
    await using var server = await LocalApiServer.StartAsync();
    var uri = new Uri(server.BaseUrl);
    var endpoint = new Endpoint
    {
        Id = "endpoint_large",
        Method = "GET",
        Scheme = uri.Scheme,
        Host = uri.Host,
        Port = uri.Port,
        Path = "/api/large",
        Classification = "likely_exists",
        Confidence = 65
    };

    var results = await new TestBatteryRunner().RunAsync(new Project
    {
        ProjectId = "large-body",
        BaseUrl = server.BaseUrl,
        Auth = new AuthConfig { Type = "none" }
    }, [endpoint], new TestRunRequest
    {
        BendTypes = ["authConsistency"],
        ParallelWorkers = 1
    }, CancellationToken.None);

    var result = results.Results.Single();
    if (Encoding.UTF8.GetByteCount(result.ResultBody) > 100 * 1024)
    {
        throw new InvalidOperationException($"expected capped resultBody <= 100 KB, got {Encoding.UTF8.GetByteCount(result.ResultBody)} bytes");
    }

    if (!result.Evidence.Contains("truncated", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("expected evidence to mention truncated response capture");
    }
}

static async Task TestBatteryRoutesBodyTestsByHttpVerb()
{
    await using var server = await LocalApiServer.StartAsync();
    var uri = new Uri(server.BaseUrl);
    var getEndpoint = new Endpoint
    {
        Id = "endpoint_get",
        Method = "GET",
        Scheme = uri.Scheme,
        Host = uri.Host,
        Port = uri.Port,
        Path = "/api/orgrow/environment/setup/{id}/{id}",
        Classification = "likely_exists",
        Confidence = 65
    };
    var postEndpoint = new Endpoint
    {
        Id = "endpoint_post",
        Method = "POST",
        Scheme = uri.Scheme,
        Host = uri.Host,
        Port = uri.Port,
        Path = "/api/orgrow/product/create",
        Classification = "likely_exists",
        Confidence = 65
    };

    var results = await new TestBatteryRunner().RunAsync(new Project
    {
        ProjectId = "verb-routing",
        BaseUrl = server.BaseUrl,
        Auth = new AuthConfig { Type = "none" }
    }, [getEndpoint, postEndpoint], new TestRunRequest
    {
        BendTypes = ["fieldSize", "requestSize", "massAssignment", "idMutation", "authConsistency"],
        FieldSizesKb = [8],
        BodySizesKb = [12],
        ParallelWorkers = 1
    }, CancellationToken.None);

    if (results.Results.Any(result => result.EndpointId == getEndpoint.Id && result.BendType is "fieldSize" or "requestSize" or "massAssignment"))
    {
        throw new InvalidOperationException("expected body-oriented tests to be skipped for GET endpoints");
    }

    if (!results.Results.Any(result => result.EndpointId == getEndpoint.Id && result.BendType == "idMutation"))
    {
        throw new InvalidOperationException("expected idMutation to run for GET endpoint with path identifiers");
    }

    var postFieldSize = results.Results.SingleOrDefault(result => result.EndpointId == postEndpoint.Id && result.BendType == "fieldSize")
        ?? throw new InvalidOperationException("expected fieldSize to run for POST endpoint");

    if (postFieldSize.Request.BodySizeBytes < 8 * 1024)
    {
        throw new InvalidOperationException($"expected fieldSize body to use configured KB size, got {postFieldSize.Request.BodySizeBytes} bytes");
    }
}

static void AssertContains(IEnumerable<string> lines, string expected)
{
    if (!lines.Any(line => string.Equals(line.Trim(), expected, StringComparison.Ordinal)))
    {
        throw new InvalidOperationException($"expected api-list.txt to contain '{expected}'");
    }
}

static string RepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend")))
    {
        dir = dir.Parent;
    }

    return dir?.FullName ?? throw new InvalidOperationException("unable to locate repository root");
}

sealed class TestEnvironment(string contentRootPath) : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "BendIt.Api.Tests";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = contentRootPath;
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ContentRootPath { get; set; } = contentRootPath;
    public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);
}

sealed class LocalApiServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    private LocalApiServer(TcpListener listener, string baseUrl)
    {
        _listener = listener;
        BaseUrl = baseUrl;
        _loop = Task.Run(ServeAsync);
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
        _stop.Cancel();
        _listener.Stop();
        try
        {
            await _loop;
        }
        catch (SocketException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _stop.Dispose();
        }
    }

    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync(_stop.Token);
            _ = Task.Run(() => RespondAsync(client), _stop.Token);
        }
    }

    private static async Task RespondAsync(TcpClient client)
    {
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        var requestLine = await reader.ReadLineAsync() ?? "";
        while (!string.IsNullOrEmpty(await reader.ReadLineAsync()))
        {
        }

        var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var path = parts.Length >= 2 ? parts[1].Split('?', 2)[0] : "/";
        if (path == "/api/orgrow/grow/isalive")
        {
            await WriteAsync(stream, 200, "true", "text/plain");
            return;
        }

        if (path is "/api/users" or "/api/orders" or "/api/items" or "/api/profile" or "/api/health" or "/health")
        {
            await WriteAsync(stream, 200, """{"ok":true}""", "application/json");
            return;
        }

        if (path is "/api/orgrow/environment/setup/1/1" or "/api/orgrow/environment/setup/2/2" or "/api/orgrow/product/create")
        {
            await WriteAsync(stream, 200, """{"ok":true}""", "application/json");
            return;
        }

        if (path == "/api/large")
        {
            await WriteAsync(stream, 200, new string('A', 130 * 1024), "text/plain");
            return;
        }

        await WriteAsync(stream, 404, """{"error":"not found"}""", "application/json");
    }

    private static async Task WriteAsync(Stream stream, int status, string body, string contentType)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var reason = status == 200 ? "OK" : "Not Found";
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header);
        await stream.WriteAsync(bytes);
    }
}
