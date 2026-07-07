using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using BendIt.Api.Models;
using EndpointModel = BendIt.Api.Models.Endpoint;

namespace BendIt.Api.TestBattery;

public sealed class TestBatteryRunner
{
    private const int MaxResultBodyBytes = 100 * 1024;
    private const int DefaultRateLimitBurstRequests = 5;
    private const int MaxRateLimitBurstRequests = 10;
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
        "inventoryExposure",
        "securityHeaders",
        "sensitiveDataExposure",
        "responseDiffing"
    ];

    public Task<ResultsDocument> RunAsync(Project project, IReadOnlyList<EndpointModel> endpoints, TestRunRequest request, CancellationToken cancellationToken)
    {
        return RunAsync(project, endpoints, request, null, cancellationToken);
    }

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
        if (bendType == "rateLimit")
        {
            return await CreateRateLimitResultAsync(client, project, testRunId, endpoint, testRequest, cancellationToken);
        }

        var body = RequestBody(bendType, testRequest);
        var url = EndpointUrl(endpoint, bendType);
        var executed = await ExecuteAsync(client, endpoint.Method, url, body, project, bendType, cancellationToken);
        var status = executed.StatusCode;
        var risk = RiskFor(bendType, status, endpoint, executed);
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
                HeadersMasked = MaskResponseHeaders(executed.ResponseHeaders),
                ContentType = executed.ContentType,
                BodySizeBytes = executed.BodySizeBytes,
                DurationMs = executed.DurationMs
            },
            ResultBody = executed.Body,
            Evidence = EvidenceFor(endpoint, bendType, status, executed),
            Outcome = outcome,
            Interesting = interesting,
            AnalysisSummary = AnalysisSummary(bendType, status, risk, interesting, executed),
            OwaspCategory = OwaspCategoryFor(bendType),
            Recommendation = RecommendationFor(bendType, risk),
            Risk = risk,
            Severity = SeverityFor(risk),
            Confidence = Math.Min(96, 58 + risk * 4),
            Reproducible = risk >= 6,
            SensitiveDataDetected = risk >= 8 || bendType == "sensitiveDataExposure" && SensitiveDataFindings(executed).Count > 0,
            TokenUsed = project.Auth.Type != "none",
            AuthContext = project.Auth,
            CreatedAt = now
        };
    }

    private static async Task<TestResult> CreateRateLimitResultAsync(HttpClient client, Project project, string testRunId, EndpointModel endpoint, TestRunRequest testRequest, CancellationToken cancellationToken)
    {
        var url = EndpointUrl(endpoint, "rateLimit");
        var burstCount = RateLimitBurstCount(testRequest);
        var baseline = await ExecuteAsync(client, endpoint.Method, url, null, project, "rateLimit", cancellationToken);
        var burst = new List<ExecutedRequest>(burstCount);
        for (var i = 0; i < burstCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            burst.Add(await ExecuteAsync(client, endpoint.Method, url, null, project, "rateLimit", cancellationToken));
        }

        var postBurst = await ExecuteAsync(client, endpoint.Method, url, null, project, "rateLimit", cancellationToken);
        var observation = RateLimitObservation.From(baseline, burst, postBurst, burstCount);
        var risk = RateLimitRisk(observation);
        var outcome = OutcomeFor("rateLimit", postBurst.StatusCode, risk);
        var interesting = IsInteresting("rateLimit", postBurst.StatusCode, risk);
        var now = DateTimeOffset.UtcNow;

        return new TestResult
        {
            Id = "result_" + ShortHash(endpoint.Id + "rateLimit" + now.ToString("O")),
            ProjectId = project.ProjectId,
            TestRunId = testRunId,
            EndpointId = endpoint.Id,
            BendType = "rateLimit",
            Category = CategoryFor("rateLimit"),
            Title = $"rateLimit burst observed HTTP {postBurst.StatusCode}",
            Method = endpoint.Method,
            Url = url,
            OriginalUrl = url,
            Mutation = RateLimitMutation(observation),
            Request = new ResultRequest
            {
                HeadersMasked = MaskedHeaders(project),
                Body = null,
                BodySizeBytes = 0
            },
            Result = new HttpResult
            {
                StatusCode = postBurst.StatusCode,
                StatusText = postBurst.StatusText,
                HeadersMasked = MaskResponseHeaders(postBurst.ResponseHeaders),
                ContentType = postBurst.ContentType,
                BodySizeBytes = postBurst.BodySizeBytes,
                DurationMs = postBurst.DurationMs
            },
            ResultBody = postBurst.Body,
            Evidence = RateLimitEvidence(endpoint, observation),
            Outcome = outcome,
            Interesting = interesting,
            AnalysisSummary = RateLimitAnalysisSummary(risk, observation),
            OwaspCategory = OwaspCategoryFor("rateLimit"),
            Recommendation = RecommendationFor("rateLimit", risk),
            Risk = risk,
            Severity = SeverityFor(risk),
            Confidence = Math.Min(96, 58 + risk * 4),
            Reproducible = risk >= 6,
            SensitiveDataDetected = false,
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

            if (bendType == "idMutation" && !HasMutableIdentifier(endpoint))
            {
                continue;
            }

            if (bendType == "inventoryExposure" && InventorySignals(endpoint).Count == 0)
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

    private static int RateLimitBurstCount(TestRunRequest request)
    {
        var requested = request.MaxRequestsPerEndpoint > 0
            ? request.MaxRequestsPerEndpoint
            : DefaultRateLimitBurstRequests;
        return Math.Clamp(requested, 2, MaxRateLimitBurstRequests);
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

    private static Dictionary<string, string> BoundaryAuthHeaders(Project project, string bendType)
    {
        if (bendType == "jwtAnalysis" && project.Auth.Type == "jwt")
        {
            return new Dictionary<string, string> { ["Authorization"] = "Bearer invalid.invalid.invalid" };
        }

        if (bendType == "cookieAnalysis" && project.Auth.Type == "cookie")
        {
            return new Dictionary<string, string> { ["Cookie"] = "bendit_invalid_auth=1" };
        }

        return [];
    }

    private static async Task<ExecutedRequest> ExecuteAsync(HttpClient client, string method, string url, string? body, Project project, string bendType, CancellationToken cancellationToken)
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

            foreach (var header in BoundaryAuthHeaders(project, bendType))
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (AllowsRequestBody(method) && body is not null)
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            foreach (var header in response.Headers)
            {
                result.ResponseHeaders[header.Key] = string.Join(", ", header.Value);
            }

            foreach (var header in response.Content.Headers)
            {
                result.ResponseHeaders[header.Key] = string.Join(", ", header.Value);
            }

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

        if (bendType == "securityHeaders")
        {
            var findings = SecurityHeaderFindings(executed.ResponseHeaders);
            return findings.Count == 0
                ? $"{endpoint.Method} {endpoint.Path} returned HTTP {status} with baseline security headers present."
                : $"{endpoint.Method} {endpoint.Path} returned HTTP {status}; " + string.Join("; ", findings) + ".";
        }

        if (bendType == "inventoryExposure")
        {
            var signals = InventorySignals(endpoint);
            return signals.Count == 0
                ? $"{endpoint.Method} {endpoint.Path} returned HTTP {status} without inventory exposure signals."
                : $"{endpoint.Method} {endpoint.Path} returned HTTP {status}; inventory signals: " + string.Join(", ", signals) + ".";
        }

        if (bendType == "sensitiveDataExposure")
        {
            var findings = SensitiveDataFindings(executed);
            return findings.Count == 0
                ? $"{endpoint.Method} {endpoint.Path} returned HTTP {status} without common sensitive data indicators."
                : $"{endpoint.Method} {endpoint.Path} returned HTTP {status}; sensitive data indicators: " + string.Join(", ", findings) + ".";
        }

        if (bendType is "authConsistency" or "jwtAnalysis" or "cookieAnalysis")
        {
            var boundary = bendType switch
            {
                "jwtAnalysis" => "malformed bearer token",
                "cookieAnalysis" => "malformed cookie",
                _ => "no authentication"
            };
            var authSuffix = executed.Truncated ? $" Response capture truncated at {MaxResultBodyBytes} bytes." : "";
            return $"{endpoint.Method} {endpoint.Path} returned HTTP {status} when tested with {boundary}.{authSuffix}";
        }

        var suffix = executed.Truncated ? $" Response capture truncated at {MaxResultBodyBytes} bytes." : "";
        return $"{endpoint.Method} {endpoint.Path} returned HTTP {status} during {bendType}.{suffix}";
    }

    private static bool AllowsRequestBody(string method)
    {
        return method.ToUpperInvariant() is "POST" or "PUT" or "PATCH";
    }

    private static int RiskFor(string bendType, int status, EndpointModel endpoint, ExecutedRequest executed)
    {
        var is2xx = status is >= 200 and < 300;
        if (bendType == "securityHeaders") return SecurityHeaderRisk(executed.ResponseHeaders);
        if (bendType == "inventoryExposure") return InventoryRisk(endpoint, status);
        if (bendType == "sensitiveDataExposure") return SensitiveDataRisk(executed);
        if (bendType is "authConsistency" or "jwtAnalysis" && is2xx && endpoint.AuthRequired) return 9;
        if (bendType == "idMutation" && is2xx && HasMutableIdentifier(endpoint)) return 9;
        if (bendType == "massAssignment" && is2xx) return 8;
        if (status >= 500) return 6;
        if (endpoint.Path.Contains("/admin", StringComparison.Ordinal) && is2xx) return 7;
        if (status >= 400) return 2;
        return 3;
    }

    private static int RateLimitRisk(RateLimitObservation observation)
    {
        if (observation.AnyRequestFailed) return 6;
        if (observation.DegradedAfterBurst) return 6;
        if (!observation.SawTooManyRequests) return 5;
        if (!observation.SawThrottlingHeaders) return 5;
        return 2;
    }

    private static Dictionary<string, object> RateLimitMutation(RateLimitObservation observation)
    {
        return new Dictionary<string, object>
        {
            ["type"] = "controlledRateLimitBurst",
            ["baselineStatus"] = observation.Baseline.StatusCode,
            ["burstRequests"] = observation.BurstRequests,
            ["burstStatuses"] = observation.BurstStatusCodes,
            ["postBurstStatus"] = observation.PostBurst.StatusCode,
            ["saw429"] = observation.SawTooManyRequests,
            ["sawThrottlingHeaders"] = observation.SawThrottlingHeaders,
            ["throttlingHeaders"] = observation.ThrottlingHeaderNames,
            ["degradedAfterBurst"] = observation.DegradedAfterBurst,
            ["degradationSignals"] = observation.DegradationSignals
        };
    }

    private static string RateLimitEvidence(EndpointModel endpoint, RateLimitObservation observation)
    {
        if (observation.AnyRequestFailed)
        {
            var errors = observation.AllRequests
                .Where(request => !string.IsNullOrWhiteSpace(request.Error))
                .Select(request => request.Error)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return $"{endpoint.Method} {endpoint.Path} rateLimit burst did not complete cleanly: {string.Join("; ", errors)}.";
        }

        var findings = new List<string>();
        if (!observation.SawTooManyRequests)
        {
            findings.Add("no HTTP 429 observed");
        }

        if (!observation.SawThrottlingHeaders)
        {
            findings.Add("no throttling headers observed");
        }
        else
        {
            findings.Add("throttling headers observed: " + string.Join(", ", observation.ThrottlingHeaderNames));
        }

        findings.Add(observation.DegradedAfterBurst
            ? "post-burst response differed from baseline: " + string.Join(", ", observation.DegradationSignals)
            : "post-burst response matched the baseline");

        return $"{endpoint.Method} {endpoint.Path} sent baseline, {observation.BurstRequests} burst request(s), and post-burst comparison; statuses: {string.Join(", ", observation.StatusCodes)}; {string.Join("; ", findings)}.";
    }

    private static string RateLimitAnalysisSummary(int risk, RateLimitObservation observation)
    {
        if (observation.AnyRequestFailed)
        {
            return "rateLimit burst could not complete all requests and should be reviewed.";
        }

        return risk >= 5
            ? "rateLimit burst found missing or degraded throttling behavior and should be reviewed."
            : "rateLimit burst observed throttling controls and normal post-burst behavior.";
    }

    private static List<string> DegradationSignals(ExecutedRequest baseline, ExecutedRequest postBurst)
    {
        var signals = new List<string>();
        if (baseline.StatusCode != postBurst.StatusCode)
        {
            signals.Add("status changed");
        }

        if (!string.Equals(baseline.ContentType, postBurst.ContentType, StringComparison.OrdinalIgnoreCase))
        {
            signals.Add("content type changed");
        }

        if (BodySizeClass(baseline.BodySizeBytes) != BodySizeClass(postBurst.BodySizeBytes))
        {
            signals.Add("body size changed");
        }

        if (!string.Equals(ShortHash(baseline.Body), ShortHash(postBurst.Body), StringComparison.Ordinal))
        {
            signals.Add("body hash changed");
        }

        if (HasThrottlingHeader(baseline.ResponseHeaders) != HasThrottlingHeader(postBurst.ResponseHeaders))
        {
            signals.Add("throttling header state changed");
        }

        return signals;
    }

    private static int BodySizeClass(int bytes)
    {
        if (bytes == 0) return 0;
        if (bytes < 1024) return 1;
        if (bytes < 10 * 1024) return 2;
        if (bytes < 100 * 1024) return 3;
        return 4;
    }

    private static bool HasThrottlingHeader(Dictionary<string, string> headers)
    {
        return ThrottlingHeaderNames(headers).Count > 0;
    }

    private static List<string> ThrottlingHeaderNames(Dictionary<string, string> headers)
    {
        return headers.Keys
            .Where(IsThrottlingHeader)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsThrottlingHeader(string name)
    {
        return name.Equals("Retry-After", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("RateLimit", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("RateLimit-Limit", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("RateLimit-Remaining", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("RateLimit-Reset", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("X-RateLimit-", StringComparison.OrdinalIgnoreCase);
    }

    private static int SecurityHeaderRisk(Dictionary<string, string> headers)
    {
        var findings = SecurityHeaderFindings(headers);
        if (findings.Any(finding => finding.Contains("cookie", StringComparison.OrdinalIgnoreCase))) return 6;
        if (findings.Any(finding => finding.Contains("verbose", StringComparison.OrdinalIgnoreCase))) return 5;
        return findings.Count > 0 ? 3 : 1;
    }

    private static List<string> SecurityHeaderFindings(Dictionary<string, string> headers)
    {
        var findings = new List<string>();
        if (!HasHeader(headers, "X-Content-Type-Options"))
        {
            findings.Add("missing X-Content-Type-Options");
        }

        if (HasHeader(headers, "Server") || HasHeader(headers, "X-Powered-By"))
        {
            findings.Add("verbose platform header exposed");
        }

        foreach (var cookie in HeaderValues(headers, "Set-Cookie"))
        {
            if (!cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase) ||
                !cookie.Contains("secure", StringComparison.OrdinalIgnoreCase) ||
                !cookie.Contains("samesite", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add("cookie missing HttpOnly, Secure, or SameSite");
                break;
            }
        }

        return findings;
    }

    private static Dictionary<string, string> MaskResponseHeaders(Dictionary<string, string> headers)
    {
        return headers.ToDictionary(
            item => item.Key,
            item => string.Equals(item.Key, "Set-Cookie", StringComparison.OrdinalIgnoreCase) ? "***" : item.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private static bool HasHeader(Dictionary<string, string> headers, string name)
    {
        return headers.Keys.Any(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> HeaderValues(Dictionary<string, string> headers, string name)
    {
        foreach (var item in headers)
        {
            if (string.Equals(item.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                yield return item.Value;
            }
        }
    }

    private static string CategoryFor(string bendType)
    {
        return bendType switch
        {
            "authConsistency" or "idMutation" or "massAssignment" => "Authorization",
            "jwtAnalysis" => "Authentication",
            "requestSize" => "Request Size",
            "sensitiveDataExposure" => "Data Exposure",
            "rateLimit" => "Rate Limiting",
            "responseDiffing" or "inventoryExposure" => "Contract Consistency",
            _ => "Input Validation"
        };
    }

    private static string OwaspCategoryFor(string bendType)
    {
        return bendType switch
        {
            "idMutation" => "API1: Broken Object Level Authorization",
            "authConsistency" or "jwtAnalysis" or "cookieAnalysis" => "API2: Broken Authentication",
            "massAssignment" or "sensitiveDataExposure" => "API3: Broken Object Property Level Authorization",
            "requestSize" or "fieldSize" or "rateLimit" or "timingAnalysis" => "API4: Unrestricted Resource Consumption",
            "httpMethodValidation" => "API5: Broken Function Level Authorization",
            "corsAnalysis" or "headerAnalysis" or "contentTypeValidation" or "securityHeaders" => "API8: Security Misconfiguration",
            "responseDiffing" or "inventoryExposure" => "API9: Improper Inventory Management",
            "payloadValidation" or "parameterPollution" => "API10: Unsafe Consumption of APIs",
            _ => "API8: Security Misconfiguration"
        };
    }

    private static string RecommendationFor(string bendType, int risk)
    {
        var prefix = risk >= 5 ? "Review and harden this behavior." : "Keep this control in place.";
        return bendType switch
        {
            "idMutation" => $"{prefix} Enforce object-level authorization on every resource lookup, not only on routes or controllers.",
            "authConsistency" or "jwtAnalysis" => $"{prefix} Require valid authentication consistently and reject missing, malformed, or expired credentials.",
            "massAssignment" => $"{prefix} Bind only explicitly allowed request fields and ignore or reject privileged object properties.",
            "sensitiveDataExposure" => $"{prefix} Return only fields required by the caller, redact secrets, and enforce response-level authorization on sensitive object properties.",
            "requestSize" or "fieldSize" => $"{prefix} Apply request size, field length, timeout, and parsing limits before business logic runs.",
            "httpMethodValidation" => $"{prefix} Restrict each endpoint to intended HTTP methods and require authorization for privileged functions.",
            "corsAnalysis" => $"{prefix} Use narrow CORS origins, methods, and credential policies.",
            "headerAnalysis" => $"{prefix} Remove verbose platform headers and add defensive response headers where appropriate.",
            "securityHeaders" => $"{prefix} Remove verbose platform headers, set X-Content-Type-Options, and harden cookie attributes when cookies are used.",
            "contentTypeValidation" => $"{prefix} Require expected content types and reject ambiguous request payload formats.",
            "rateLimit" => $"{prefix} Add per-user and per-origin throttling for sensitive or expensive endpoints.",
            "responseDiffing" => $"{prefix} Maintain a current endpoint inventory and investigate undocumented live routes.",
            "inventoryExposure" => $"{prefix} Remove or protect legacy, debug, documentation, and environment-only routes, and keep a reviewed API inventory with deprecation owners.",
            _ => $"{prefix} Validate inputs, authorization, and response handling for this endpoint."
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
        var path = bendType == "idMutation"
            ? MutateIdentifierPath(endpoint.Path, "2")
            : ConcretePath(endpoint.Path, "1");
        var query = bendType == "idMutation" ? IdentifierQuery(endpoint.QueryParams, "2") : "";
        return endpoint.Scheme + "://" + endpoint.Host + port + path + query;
    }

    private static string ConcretePath(string path, string replacement)
    {
        return System.Text.RegularExpressions.Regex.Replace(path, "\\{[^/]+\\}", replacement);
    }

    private static string MutateIdentifierPath(string path, string replacement)
    {
        var concrete = ConcretePath(path, replacement);
        concrete = System.Text.RegularExpressions.Regex.Replace(concrete, "(?<=/)\\d+(?=/|$)", replacement);
        concrete = System.Text.RegularExpressions.Regex.Replace(
            concrete,
            "(?<=/)[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}(?=/|$)",
            "00000000-0000-0000-0000-000000000002");
        return concrete;
    }

    private static bool HasMutableIdentifier(EndpointModel endpoint)
    {
        return endpoint.Path.Contains('{', StringComparison.Ordinal) ||
               System.Text.RegularExpressions.Regex.IsMatch(endpoint.Path, "/\\d+(?:/|$)") ||
               System.Text.RegularExpressions.Regex.IsMatch(endpoint.Path, "/[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}(?:/|$)") ||
               endpoint.QueryParams.Any(IsIdentifierParam);
    }

    private static string IdentifierQuery(IEnumerable<string> queryParams, string replacement)
    {
        var pairs = queryParams
            .Where(IsIdentifierParam)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => Uri.EscapeDataString(name) + "=" + Uri.EscapeDataString(replacement))
            .ToList();
        return pairs.Count == 0 ? "" : "?" + string.Join("&", pairs);
    }

    private static bool IsIdentifierParam(string name)
    {
        return name.Equals("id", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("tenant", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("owner", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("account", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("resource", StringComparison.OrdinalIgnoreCase);
    }

    private static int InventoryRisk(EndpointModel endpoint, int status)
    {
        var signals = InventorySignals(endpoint);
        if (signals.Count == 0) return status >= 400 ? 1 : 2;
        if (status is >= 200 and < 300)
        {
            return signals.Any(IsHighInventorySignal) ? 7 : 5;
        }

        return status >= 400 ? 2 : 3;
    }

    private static List<string> InventorySignals(EndpointModel endpoint)
    {
        var signals = new List<string>();
        var path = endpoint.Path.ToLowerInvariant();
        var sourceDetail = endpoint.SourceDetail?.ToLowerInvariant() ?? "";

        if (System.Text.RegularExpressions.Regex.IsMatch(path, @"(^|/)(api/)?v\d+($|/)"))
        {
            signals.Add("versioned API route");
        }

        AddInventorySignal(signals, path, "legacy", "legacy route");
        AddInventorySignal(signals, path, "deprecated", "deprecated route");
        AddInventorySignal(signals, path, "beta", "beta route");
        AddInventorySignal(signals, path, "experimental", "experimental route");
        AddInventorySignal(signals, path, "internal", "internal route");
        AddInventorySignal(signals, path, "debug", "debug route");
        AddInventorySignal(signals, path, "staging", "staging route");
        AddInventorySignal(signals, path, "test", "test route");
        AddInventorySignal(signals, path, "mock", "mock route");
        AddInventorySignal(signals, path, "tmp", "temporary route");
        AddInventorySignal(signals, path, "swagger", "API documentation route");
        AddInventorySignal(signals, path, "openapi", "API documentation route");
        AddInventorySignal(signals, path, "actuator", "operational route");
        AddInventorySignal(signals, path, "metrics", "operational route");
        AddInventorySignal(signals, path, "env", "environment route");
        AddInventorySignal(signals, path, "graphql", "GraphQL route");

        if (sourceDetail.Contains("swagger", StringComparison.Ordinal) || sourceDetail.Contains("openapi", StringComparison.Ordinal))
        {
            AddDistinct(signals, "documented by API spec");
        }

        return signals;
    }

    private static void AddInventorySignal(List<string> signals, string path, string token, string signal)
    {
        if (System.Text.RegularExpressions.Regex.IsMatch(path, $@"(^|/|[-_.]){System.Text.RegularExpressions.Regex.Escape(token)}($|/|[-_.])"))
        {
            AddDistinct(signals, signal);
        }
    }

    private static void AddDistinct(List<string> signals, string signal)
    {
        if (!signals.Contains(signal, StringComparer.OrdinalIgnoreCase))
        {
            signals.Add(signal);
        }
    }

    private static bool IsHighInventorySignal(string signal)
    {
        return signal.Contains("legacy", StringComparison.OrdinalIgnoreCase) ||
               signal.Contains("deprecated", StringComparison.OrdinalIgnoreCase) ||
               signal.Contains("internal", StringComparison.OrdinalIgnoreCase) ||
               signal.Contains("debug", StringComparison.OrdinalIgnoreCase) ||
               signal.Contains("environment", StringComparison.OrdinalIgnoreCase) ||
               signal.Contains("operational", StringComparison.OrdinalIgnoreCase);
    }

    private static int SensitiveDataRisk(ExecutedRequest executed)
    {
        var findings = SensitiveDataFindings(executed);
        if (findings.Count == 0) return 2;
        if (findings.Any(IsCredentialExposure)) return 8;
        return 6;
    }

    private static List<string> SensitiveDataFindings(ExecutedRequest executed)
    {
        var findings = new List<string>();
        var body = executed.Body;
        if (string.IsNullOrWhiteSpace(body))
        {
            return findings;
        }

        AddSensitivePattern(findings, body, "\"(?:access[_-]?token|refresh[_-]?token|id[_-]?token|api[_-]?key|secret|password|session[_-]?id)\"\\s*:", "credential-shaped response field");
        AddSensitivePattern(findings, body, "[A-Z0-9._%+-]+@[A-Z0-9.-]+\\.[A-Z]{2,}", "email address");
        AddSensitivePattern(findings, body, "\"(?:ssn|socialSecurityNumber|taxId|cpf|nationalId)\"\\s*:", "national identifier field");
        AddSensitivePattern(findings, body, "\"(?:phone|mobile|telephone)\"\\s*:", "phone field");

        return findings;
    }

    private static void AddSensitivePattern(List<string> findings, string body, string pattern, string finding)
    {
        if (System.Text.RegularExpressions.Regex.IsMatch(body, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            AddDistinct(findings, finding);
        }
    }

    private static bool IsCredentialExposure(string finding)
    {
        return finding.Contains("credential", StringComparison.OrdinalIgnoreCase);
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
        public Dictionary<string, string> ResponseHeaders { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? Error { get; set; }
    }

    private sealed class RateLimitObservation
    {
        public required ExecutedRequest Baseline { get; init; }
        public required List<ExecutedRequest> Burst { get; init; }
        public required ExecutedRequest PostBurst { get; init; }
        public required int BurstRequests { get; init; }
        public required List<string> DegradationSignals { get; init; }
        public required List<string> ThrottlingHeaderNames { get; init; }
        public List<ExecutedRequest> AllRequests => [Baseline, .. Burst, PostBurst];
        public List<int> BurstStatusCodes => Burst.Select(request => request.StatusCode).ToList();
        public List<int> StatusCodes => AllRequests.Select(request => request.StatusCode).ToList();
        public bool SawTooManyRequests => AllRequests.Any(request => request.StatusCode == 429);
        public bool SawThrottlingHeaders => ThrottlingHeaderNames.Count > 0;
        public bool DegradedAfterBurst => DegradationSignals.Count > 0;
        public bool AnyRequestFailed => AllRequests.Any(request => !string.IsNullOrEmpty(request.Error));

        public static RateLimitObservation From(ExecutedRequest baseline, List<ExecutedRequest> burst, ExecutedRequest postBurst, int burstRequests)
        {
            var throttlingHeaders = new List<string>();
            foreach (var request in new[] { baseline }.Concat(burst).Append(postBurst))
            {
                foreach (var header in ThrottlingHeaderNames(request.ResponseHeaders))
                {
                    if (!throttlingHeaders.Contains(header, StringComparer.OrdinalIgnoreCase))
                    {
                        throttlingHeaders.Add(header);
                    }
                }
            }

            throttlingHeaders.Sort(StringComparer.OrdinalIgnoreCase);

            return new RateLimitObservation
            {
                Baseline = baseline,
                Burst = burst,
                PostBurst = postBurst,
                BurstRequests = burstRequests,
                DegradationSignals = DegradationSignals(baseline, postBurst),
                ThrottlingHeaderNames = throttlingHeaders
            };
        }
    }
}
