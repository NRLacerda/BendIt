using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using BendIt.Api.Models;
using EndpointModel = BendIt.Api.Models.Endpoint;

namespace BendIt.Api.TestBattery;

public sealed class TestBatteryRunner
{
    private const int MaxResultBodyBytes = 100 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly string[] DefaultBendTypes =
    [
        "authConsistency",
        "jwtAnalysis",
        "httpMethodValidation",
        "payloadValidation",
        "requestSize",
        "fieldSize",
        "massAssignment",
        "idMutation",
        "responseDiffing"
    ];

    public async Task<ResultsDocument> RunAsync(
        Project project,
        IReadOnlyList<EndpointModel> endpoints,
        TestRunRequest request,
        Func<int, int, int, Task>? progressCallback,
        CancellationToken cancellationToken)
    {
        IEnumerable<string> bendTypes = request.BendTypes.Count == 0 ? DefaultBendTypes : request.BendTypes;
        var plannedJobs = endpoints
            .Where(endpoint => !Excluded(endpoint.Path, request.ExcludedPathPatterns))
            .SelectMany(endpoint => ApplicableBendTypes(endpoint, bendTypes))
            .Count();
        var results = new List<TestResult>();
        var testRunId = "run_" + ShortHash(project.ProjectId + DateTimeOffset.UtcNow.ToString("O"));
        using var client = new HttpClient { Timeout = RequestTimeout };

        foreach (var endpoint in endpoints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Excluded(endpoint.Path, request.ExcludedPathPatterns))
            {
                continue;
            }

            foreach (var bendType in ApplicableBendTypes(endpoint, bendTypes))
            {
                var result = await CreateResultAsync(client, project, testRunId, endpoint, bendType, request, cancellationToken);
                results.Add(result);
                if (progressCallback is not null)
                {
                    await progressCallback(results.Count, plannedJobs, results.Count(item => item.Interesting));
                }
            }
        }

        return new ResultsDocument
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            ProjectId = project.ProjectId,
            Results = results
        };
    }

    private static async Task<TestResult> CreateResultAsync(HttpClient client, Project project, string testRunId, EndpointModel endpoint, string bendType, TestRunRequest testRequest, CancellationToken cancellationToken)
    {
        var body = RequestBody(bendType, testRequest);
        var url = EndpointUrl(endpoint, bendType);
        var executed = await ExecuteAsync(client, endpoint.Method, url, body, project, cancellationToken);
        var status = executed.StatusCode;
        var risk = RiskFor(bendType, status, endpoint);
        var outcome = OutcomeFor(bendType, status, risk);
        var interesting = IsInteresting(bendType, status, risk);
        var now = DateTimeOffset.UtcNow;

        return new TestResult
        {
            Id = "result_" + ShortHash(endpoint.Id + bendType + now.ToString("O")),
            ProjectId = project.ProjectId,
            TestRunId = testRunId,
            EndpointId = endpoint.Id,
            BendType = bendType,
            Category = CategoryFor(bendType),
            Title = $"{bendType} returned HTTP {status}",
            Method = endpoint.Method,
            Url = url,
            OriginalUrl = url,
            Mutation = MutationFor(bendType, testRequest),
            Request = new ResultRequest
            {
                HeadersMasked = MaskedHeaders(project),
                Body = body,
                BodySizeBytes = body?.Length ?? 0
            },
            Result = new HttpResult
            {
                StatusCode = status,
                StatusText = executed.StatusText,
                ContentType = executed.ContentType,
                BodySizeBytes = executed.BodySizeBytes,
                DurationMs = executed.DurationMs
            },
            ResultBody = executed.Body,
            Evidence = EvidenceFor(endpoint, bendType, status, executed),
            Outcome = outcome,
            Interesting = interesting,
            AnalysisSummary = AnalysisSummary(bendType, status, risk, interesting, executed),
            Risk = risk,
            Severity = SeverityFor(risk),
            Confidence = Math.Min(96, 58 + risk * 4),
            Reproducible = risk >= 6,
            SensitiveDataDetected = risk >= 8,
            TokenUsed = project.Auth.Type != "none",
            AuthContext = project.Auth,
            CreatedAt = now
        };
    }

    private static string OutcomeFor(string bendType, int status, int risk)
    {
        if (risk >= 7) return "finding";
        if (risk >= 5) return "suspicious";
        if (bendType == "requestSize" && status == 413) return "expected-control";
        if (status is >= 400 and < 500) return "blocked";
        return "observed";
    }

    private static bool IsInteresting(string bendType, int status, int risk)
    {
        return risk >= 5 || bendType == "requestSize" && status >= 500;
    }

    private static string AnalysisSummary(string bendType, int status, int risk, bool interesting, ExecutedRequest executed)
    {
        if (!string.IsNullOrEmpty(executed.Error))
        {
            return $"{bendType} did not complete: {executed.Error}.";
        }

        if (executed.Truncated)
        {
            return $"{bendType} returned HTTP {status}; response capture was capped at {MaxResultBodyBytes} bytes.";
        }

        return interesting
            ? $"{bendType} produced a risk {risk} result with HTTP {status} and should be reviewed."
            : $"{bendType} returned HTTP {status} and is stored as raw evidence.";
    }

    private static IEnumerable<string> ApplicableBendTypes(EndpointModel endpoint, IEnumerable<string> bendTypes)
    {
        foreach (var bendType in bendTypes)
        {
            if (RequiresRequestBody(bendType) && !AllowsRequestBody(endpoint.Method))
            {
                continue;
            }

            if (bendType == "idMutation" && !endpoint.Path.Contains('{', StringComparison.Ordinal))
            {
                continue;
            }

            yield return bendType;
        }
    }

    private static bool RequiresRequestBody(string bendType)
    {
        return bendType is "payloadValidation" or "requestSize" or "fieldSize" or "massAssignment" or "contentTypeValidation";
    }

    private static string? RequestBody(string bendType, TestRunRequest request)
    {
        return bendType switch
        {
            "massAssignment" => """{"role":"ADMIN","isAdmin":true,"tenantId":"other-tenant"}""",
            "payloadValidation" => """{"payload":""",
            "contentTypeValidation" => """{"payload":"content-type-check"}""",
            "fieldSize" => JsonPayload("description", LargestKb(request.FieldSizesKb, 50)),
            "requestSize" => JsonPayload("payload", LargestKb(request.BodySizesKb, 100)),
            _ => null
        };
    }

    private static Dictionary<string, object> MutationFor(string bendType, TestRunRequest request)
    {
        return bendType switch
        {
            "idMutation" => new Dictionary<string, object> { ["type"] = "pathIdMutation", ["field"] = "id", ["originalValue"] = "123", ["mutatedValue"] = "124" },
            "massAssignment" => new Dictionary<string, object> { ["type"] = "extraFields", ["fields"] = new[] { "role", "isAdmin", "tenantId" } },
            "fieldSize" => new Dictionary<string, object> { ["type"] = "fieldExpansion", ["sizeKb"] = LargestKb(request.FieldSizesKb, 50) },
            "requestSize" => new Dictionary<string, object> { ["type"] = "bodyExpansion", ["sizeKb"] = LargestKb(request.BodySizesKb, 100) },
            _ => new Dictionary<string, object> { ["type"] = bendType }
        };
    }

    private static string JsonPayload(string fieldName, int sizeKb)
    {
        return "{\"" + fieldName + "\":\"" + new string('A', Math.Max(1, sizeKb) * 1024) + "\"}";
    }

    private static int LargestKb(IEnumerable<int> sizes, int fallback)
    {
        var valid = sizes.Where(size => size > 0).DefaultIfEmpty(fallback);
        return Math.Min(valid.Max(), 512);
    }

    private static Dictionary<string, string> MaskedHeaders(Project project)
    {
        var headers = new Dictionary<string, string> { ["Accept"] = "application/json" };
        switch (project.Auth.Type)
        {
            case "jwt":
                headers["Authorization"] = "Bearer ***";
                break;
            case "cookie":
                headers["Cookie"] = "***";
                break;
            case "headers":
                foreach (var header in project.Auth.HeadersMasked ?? [])
                {
                    headers[header.Name] = header.Value;
                }
                break;
        }

        return headers;
    }

    private static async Task<ExecutedRequest> ExecuteAsync(HttpClient client, string method, string url, string? body, Project project, CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var result = new ExecutedRequest();
        try
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), url);
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            foreach (var header in project.Headers ?? [])
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (AllowsRequestBody(method) && body is not null)
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var (responseBody, bodySizeBytes, truncated) = await ReadBodyAsync(response.Content, cancellationToken);
            result.StatusCode = (int)response.StatusCode;
            result.StatusText = response.ReasonPhrase ?? StatusText((int)response.StatusCode);
            result.ContentType = response.Content.Headers.ContentType?.ToString() ?? "";
            result.Body = responseBody;
            result.BodySizeBytes = bodySizeBytes;
            result.Truncated = truncated;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            result.StatusCode = 0;
            result.StatusText = "Request Failed";
            result.Error = ex.Message;
            result.Body = "";
        }
        finally
        {
            started.Stop();
            result.DurationMs = (int)Math.Min(started.ElapsedMilliseconds, int.MaxValue);
        }

        return result;
    }

    private static async Task<(string Body, int BodySizeBytes, bool Truncated)> ReadBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        var totalRead = 0;
        var truncated = false;

        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
            var remaining = MaxResultBodyBytes - (int)memory.Length;
            if (remaining > 0)
            {
                memory.Write(buffer, 0, Math.Min(read, remaining));
            }

            if (totalRead >= MaxResultBodyBytes)
            {
                truncated = await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken) > 0;
                break;
            }
        }

        return (Encoding.UTF8.GetString(memory.ToArray()), totalRead, truncated);
    }

    private static string EvidenceFor(EndpointModel endpoint, string bendType, int status, ExecutedRequest executed)
    {
        if (!string.IsNullOrEmpty(executed.Error))
        {
            return $"{endpoint.Method} {endpoint.Path} failed during {bendType}: {executed.Error}.";
        }

        var suffix = executed.Truncated ? $" Response capture truncated at {MaxResultBodyBytes} bytes." : "";
        return $"{endpoint.Method} {endpoint.Path} returned HTTP {status} during {bendType}.{suffix}";
    }

    private static bool AllowsRequestBody(string method)
    {
        return method.ToUpperInvariant() is "POST" or "PUT" or "PATCH";
    }

    private static int RiskFor(string bendType, int status, EndpointModel endpoint)
    {
        var is2xx = status is >= 200 and < 300;
        if (bendType is "authConsistency" or "jwtAnalysis" && is2xx && endpoint.AuthRequired) return 9;
        if (bendType == "idMutation" && is2xx && endpoint.Path.Contains("{id}", StringComparison.Ordinal)) return 9;
        if (bendType == "massAssignment" && is2xx) return 8;
        if (status >= 500) return 6;
        if (bendType == "rateLimit" && status != 429) return 5;
        if (endpoint.Path.Contains("/admin", StringComparison.Ordinal) && is2xx) return 7;
        if (status >= 400) return 2;
        return 3;
    }

    private static string CategoryFor(string bendType)
    {
        return bendType switch
        {
            "authConsistency" or "idMutation" or "massAssignment" => "Authorization",
            "jwtAnalysis" => "Authentication",
            "requestSize" => "Request Size",
            "rateLimit" => "Rate Limiting",
            "responseDiffing" => "Contract Consistency",
            _ => "Input Validation"
        };
    }

    private static string SeverityFor(int risk)
    {
        return risk switch
        {
            >= 9 => "Critical",
            >= 7 => "High",
            >= 5 => "Medium",
            >= 3 => "Low",
            _ => "Informational"
        };
    }

    private static string EndpointUrl(EndpointModel endpoint, string bendType)
    {
        var port = endpoint.Port is > 0 ? ":" + endpoint.Port.Value : "";
        return endpoint.Scheme + "://" + endpoint.Host + port + ConcretePath(endpoint.Path, bendType == "idMutation" ? "2" : "1");
    }

    private static string ConcretePath(string path, string replacement)
    {
        return System.Text.RegularExpressions.Regex.Replace(path, "\\{[^/]+\\}", replacement);
    }

    private static string StatusText(int status)
    {
        return status switch
        {
            200 => "OK",
            201 => "Created",
            400 => "Bad Request",
            404 => "Not Found",
            413 => "Payload Too Large",
            429 => "Too Many Requests",
            500 => "Internal Server Error",
            _ => "HTTP"
        };
    }

    private static bool Excluded(string path, IEnumerable<string> patterns)
    {
        return patterns.Any(pattern => !string.IsNullOrEmpty(pattern) && path.Contains(pattern, StringComparison.Ordinal));
    }

    private static string ShortHash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..12];
    }

    private sealed class ExecutedRequest
    {
        public int StatusCode { get; set; }
        public string StatusText { get; set; } = "";
        public string ContentType { get; set; } = "";
        public string Body { get; set; } = "";
        public int BodySizeBytes { get; set; }
        public int DurationMs { get; set; }
        public bool Truncated { get; set; }
        public string? Error { get; set; }
    }
}
