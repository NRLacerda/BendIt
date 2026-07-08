using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using BendIt.Api.Models;
using EndpointModel = BendIt.Api.Models.Endpoint;

namespace BendIt.Api.TestBattery;

internal static class RobustnessTestHelpers
{
    public const int MaxResultBodyBytes = 100 * 1024;
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    public static bool RequiresRequestBody(string bendType)
    {
        return bendType is "payloadValidation" or "requestSize" or "fieldSize" or "massAssignment" or "contentTypeValidation";
    }

    public static bool AllowsRequestBody(string method)
    {
        return method.ToUpperInvariant() is "POST" or "PUT" or "PATCH";
    }

    public static string JsonPayload(string fieldName, int sizeKb)
    {
        return "{\"" + fieldName + "\":\"" + new string('A', Math.Max(1, sizeKb) * 1024) + "\"}";
    }

    public static int LargestKb(IEnumerable<int> sizes, int fallback)
    {
        var valid = sizes.Where(size => size > 0).DefaultIfEmpty(fallback);
        return Math.Min(valid.Max(), 512);
    }

    public static Dictionary<string, string> MaskedHeaders(Project project)
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

    public static Dictionary<string, string> BoundaryAuthHeaders(Project project, string bendType)
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

    public static async Task<ExecutedRequest> ExecuteAsync(HttpClient client, string method, string url, string? body, Project project, string bendType, CancellationToken cancellationToken)
    {
        return await ExecuteAsync(client, method, url, body, project, bendType, "application/json", true, cancellationToken);
    }

    public static async Task<ExecutedRequest> ExecuteAsync(
        HttpClient client,
        string method,
        string url,
        string? body,
        Project project,
        string bendType,
        string? requestContentType,
        bool sendContentType,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(
            client,
            method,
            url,
            body,
            project,
            bendType,
            requestContentType,
            sendContentType,
            [],
            [],
            cancellationToken);
    }

    public static async Task<ExecutedRequest> ExecuteAsync(
        HttpClient client,
        string method,
        string url,
        string? body,
        Project project,
        string bendType,
        string? requestContentType,
        bool sendContentType,
        IEnumerable<KeyValuePair<string, string>> extraHeaders,
        IEnumerable<string> excludedProjectHeaders,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var result = new ExecutedRequest();
        try
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), url);
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            var excluded = excludedProjectHeaders.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var header in project.Headers ?? [])
            {
                if (excluded.Contains(header.Key))
                {
                    continue;
                }

                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            foreach (var header in BoundaryAuthHeaders(project, bendType))
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            foreach (var header in extraHeaders)
            {
                request.Headers.Remove(header.Key);
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (AllowsRequestBody(method) && body is not null)
            {
                request.Content = new StringContent(body, Encoding.UTF8);
                if (sendContentType && !string.IsNullOrWhiteSpace(requestContentType))
                {
                    request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(requestContentType);
                }
                else
                {
                    request.Content.Headers.ContentType = null;
                }
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

    public static async Task<(string Body, int BodySizeBytes, bool Truncated)> ReadBodyAsync(HttpContent content, CancellationToken cancellationToken)
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

    public static string OutcomeFor(string bendType, int status, int risk)
    {
        if (risk >= 7) return "finding";
        if (risk >= 5) return "suspicious";
        if (bendType == "requestSize" && status == 413) return "expected-control";
        if (status is >= 400 and < 500) return "blocked";
        return "observed";
    }

    public static bool IsInteresting(string bendType, int status, int risk)
    {
        return risk >= 5 || bendType == "requestSize" && status >= 500;
    }

    public static string AnalysisSummary(string bendType, int status, int risk, bool interesting, ExecutedRequest executed)
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

    public static string EvidenceFor(EndpointModel endpoint, string bendType, int status, ExecutedRequest executed)
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

    public static int RiskFor(string bendType, int status, EndpointModel endpoint, ExecutedRequest executed)
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

    public static int SecurityHeaderRisk(Dictionary<string, string> headers)
    {
        var findings = SecurityHeaderFindings(headers);
        if (findings.Any(finding => finding.Contains("cookie", StringComparison.OrdinalIgnoreCase))) return 6;
        if (findings.Any(finding => finding.Contains("verbose", StringComparison.OrdinalIgnoreCase))) return 5;
        return findings.Count > 0 ? 3 : 1;
    }

    public static List<string> SecurityHeaderFindings(Dictionary<string, string> headers)
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

    public static Dictionary<string, string> MaskResponseHeaders(Dictionary<string, string> headers)
    {
        return headers.ToDictionary(
            item => item.Key,
            item => string.Equals(item.Key, "Set-Cookie", StringComparison.OrdinalIgnoreCase) ? "***" : item.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    public static string CategoryFor(string bendType)
    {
        return bendType switch
        {
            "authConsistency" or "idMutation" or "massAssignment" => "Authorization",
            "jwtAnalysis" => "Authentication",
            "requestSize" => "Request Size",
            "sensitiveDataExposure" or "errorDisclosure" => "Data Exposure",
            "rateLimit" => "Rate Limiting",
            "responseDiffing" or "inventoryExposure" => "Contract Consistency",
            _ => "Input Validation"
        };
    }

    public static string OwaspCategoryFor(string bendType)
    {
        return bendType switch
        {
            "idMutation" => "API1: Broken Object Level Authorization",
            "authConsistency" or "jwtAnalysis" or "cookieAnalysis" => "API2: Broken Authentication",
            "massAssignment" or "sensitiveDataExposure" => "API3: Broken Object Property Level Authorization",
            "requestSize" or "fieldSize" or "rateLimit" or "timingAnalysis" => "API4: Unrestricted Resource Consumption",
            "httpMethodValidation" => "API5: Broken Function Level Authorization",
            "corsAnalysis" or "headerAnalysis" or "contentTypeValidation" or "securityHeaders" or "errorDisclosure" => "API8: Security Misconfiguration",
            "responseDiffing" or "inventoryExposure" => "API9: Improper Inventory Management",
            "payloadValidation" or "parameterPollution" => "API10: Unsafe Consumption of APIs",
            _ => "API8: Security Misconfiguration"
        };
    }

    public static string RecommendationFor(string bendType, int risk)
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
            "parameterPollution" => $"{prefix} Reject duplicated request parameters for security-sensitive fields, canonicalize repeated keys before authorization decisions, and return validation errors for ambiguous input.",
            "errorDisclosure" => $"{prefix} Return sanitized problem details to callers, disable debug error pages, log detailed exceptions server-side, and avoid exposing internal fields or dependency failures.",
            "rateLimit" => $"{prefix} Add per-user and per-origin throttling for sensitive or expensive endpoints.",
            "responseDiffing" => $"{prefix} Maintain a current endpoint inventory and investigate undocumented live routes.",
            "inventoryExposure" => $"{prefix} Remove or protect legacy, debug, documentation, and environment-only routes, and keep a reviewed API inventory with deprecation owners.",
            _ => $"{prefix} Validate inputs, authorization, and response handling for this endpoint."
        };
    }

    public static string SeverityFor(int risk)
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

    public static string EndpointUrl(EndpointModel endpoint, string bendType)
    {
        var port = endpoint.Port is > 0 ? ":" + endpoint.Port.Value : "";
        var path = bendType == "idMutation"
            ? MutateIdentifierPath(endpoint.Path, "2")
            : ConcretePath(endpoint.Path, "1");
        var query = bendType == "idMutation" ? IdentifierQuery(endpoint.QueryParams, "2") : "";
        return endpoint.Scheme + "://" + endpoint.Host + port + path + query;
    }

    public static string AppendQuery(string url, IEnumerable<KeyValuePair<string, string>> parameters)
    {
        var pairs = parameters
            .Select(parameter => Uri.EscapeDataString(parameter.Key) + "=" + Uri.EscapeDataString(parameter.Value))
            .ToList();
        if (pairs.Count == 0)
        {
            return url;
        }

        var separator = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return url + separator + string.Join("&", pairs);
    }

    public static string ConcretePath(string path, string replacement)
    {
        return System.Text.RegularExpressions.Regex.Replace(path, "\\{[^/]+\\}", replacement);
    }

    public static string MutateIdentifierPath(string path, string replacement)
    {
        var concrete = ConcretePath(path, replacement);
        concrete = System.Text.RegularExpressions.Regex.Replace(concrete, "(?<=/)\\d+(?=/|$)", replacement);
        concrete = System.Text.RegularExpressions.Regex.Replace(
            concrete,
            "(?<=/)[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}(?=/|$)",
            "00000000-0000-0000-0000-000000000002");
        return concrete;
    }

    public static bool HasMutableIdentifier(EndpointModel endpoint)
    {
        return endpoint.Path.Contains('{', StringComparison.Ordinal) ||
               System.Text.RegularExpressions.Regex.IsMatch(endpoint.Path, "/\\d+(?:/|$)") ||
               System.Text.RegularExpressions.Regex.IsMatch(endpoint.Path, "/[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}(?:/|$)") ||
               endpoint.QueryParams.Any(IsIdentifierParam);
    }

    public static string IdentifierQuery(IEnumerable<string> queryParams, string replacement)
    {
        var pairs = queryParams
            .Where(IsIdentifierParam)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => Uri.EscapeDataString(name) + "=" + Uri.EscapeDataString(replacement))
            .ToList();
        return pairs.Count == 0 ? "" : "?" + string.Join("&", pairs);
    }

    public static bool IsIdentifierParam(string name)
    {
        return name.Equals("id", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("tenant", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("owner", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("account", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("resource", StringComparison.OrdinalIgnoreCase);
    }

    public static int InventoryRisk(EndpointModel endpoint, int status)
    {
        var signals = InventorySignals(endpoint);
        if (signals.Count == 0) return status >= 400 ? 1 : 2;
        if (status is >= 200 and < 300)
        {
            return signals.Any(IsHighInventorySignal) ? 7 : 5;
        }

        return status >= 400 ? 2 : 3;
    }

    public static List<string> InventorySignals(EndpointModel endpoint)
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

    public static int SensitiveDataRisk(ExecutedRequest executed)
    {
        var findings = SensitiveDataFindings(executed);
        if (findings.Count == 0) return 2;
        if (findings.Any(IsCredentialExposure)) return 8;
        return 6;
    }

    public static List<string> SensitiveDataFindings(ExecutedRequest executed)
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

    public static bool Excluded(string path, IEnumerable<string> patterns)
    {
        return patterns.Any(pattern => !string.IsNullOrEmpty(pattern) && path.Contains(pattern, StringComparison.Ordinal));
    }

    public static string ShortHash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..12];
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
}
