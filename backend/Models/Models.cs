using System.Text.Json.Serialization;

namespace BendIt.Api.Models;

public sealed class Project
{
    [JsonPropertyName("projectId")] public string ProjectId { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("baseUrl")] public string BaseUrl { get; set; } = "";
    [JsonPropertyName("isWebPage")] public bool IsWebPage { get; set; }
    [JsonPropertyName("headers")] public Dictionary<string, string>? Headers { get; set; }
    [JsonPropertyName("auth")] public AuthConfig Auth { get; set; } = new();
    [JsonPropertyName("outputDir")] public string OutputDir { get; set; } = "";
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; set; }
    [JsonPropertyName("updatedAt")] public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AuthConfig
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("headerName")] public string? HeaderName { get; set; }
    [JsonPropertyName("scheme")] public string? Scheme { get; set; }
    [JsonPropertyName("tokenMasked")] public string? TokenMasked { get; set; }
    [JsonPropertyName("cookieMasked")] public string? CookieMasked { get; set; }
    [JsonPropertyName("headersMasked")] public List<MaskedHeaderValue>? HeadersMasked { get; set; }
}

public sealed class MaskedHeaderValue
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("value")] public string Value { get; set; } = "";
}

public sealed class Endpoint
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("method")] public string Method { get; set; } = "";
    [JsonPropertyName("scheme")] public string Scheme { get; set; } = "";
    [JsonPropertyName("host")] public string Host { get; set; } = "";
    [JsonPropertyName("port")] public int? Port { get; set; }
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("queryParams")] public List<string> QueryParams { get; set; } = [];
    [JsonPropertyName("source")] public List<string> Source { get; set; } = [];
    [JsonPropertyName("sourceDetail")] public string? SourceDetail { get; set; }
    [JsonPropertyName("authRequired")] public bool AuthRequired { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("statusCode")] public int StatusCode { get; set; }
    [JsonPropertyName("contentType")] public string? ContentType { get; set; }
    [JsonPropertyName("responseLength")] public long ResponseLength { get; set; }
    [JsonPropertyName("responseHash")] public string? ResponseHash { get; set; }
    [JsonPropertyName("confidence")] public int Confidence { get; set; }
    [JsonPropertyName("classification")] public string? Classification { get; set; }
    [JsonPropertyName("protected")] public bool Protected { get; set; }
    [JsonPropertyName("redirectedTo")] public string? RedirectedTo { get; set; }
    [JsonPropertyName("requestSchemaId")] public string? RequestSchemaId { get; set; }
    [JsonPropertyName("responseSchemaId")] public string? ResponseSchemaId { get; set; }
    [JsonPropertyName("firstSeenAt")] public DateTimeOffset FirstSeenAt { get; set; }
    [JsonPropertyName("lastSeenAt")] public DateTimeOffset LastSeenAt { get; set; }
    [JsonPropertyName("verifiedAt")] public DateTimeOffset? VerifiedAt { get; set; }
}

public sealed class EndpointsDocument
{
    [JsonPropertyName("generatedAt")] public DateTimeOffset GeneratedAt { get; set; }
    [JsonPropertyName("endpoints")] public List<Endpoint> Endpoints { get; set; } = [];
}

public sealed class DiscoveryRequest
{
    [JsonPropertyName("useNativeApiList")] public bool UseNativeApiList { get; set; }
    [JsonPropertyName("useSpecDiscovery")] public bool UseSpecDiscovery { get; set; }
    [JsonPropertyName("apiList")] public List<string> ApiList { get; set; } = [];
    [JsonPropertyName("maxWorkers")] public int MaxWorkers { get; set; }
    [JsonPropertyName("timeoutSeconds")] public int TimeoutSeconds { get; set; }
    [JsonPropertyName("maxBodyBytes")] public long MaxBodyBytes { get; set; }
    [JsonPropertyName("followRedirects")] public bool FollowRedirects { get; set; }
}

public sealed class TestRunRequest
{
    [JsonPropertyName("bendTypes")] public List<string> BendTypes { get; set; } = [];
    [JsonPropertyName("maxRequestsPerEndpoint")] public int MaxRequestsPerEndpoint { get; set; }
    [JsonPropertyName("parallelWorkers")] public int ParallelWorkers { get; set; }
    [JsonPropertyName("downDetectionThreshold")] public int? DownDetectionThreshold { get; set; }
    [JsonPropertyName("fieldSizesKb")] public List<int> FieldSizesKb { get; set; } = [];
    [JsonPropertyName("bodySizesKb")] public List<int> BodySizesKb { get; set; } = [];
    [JsonPropertyName("excludedPathPatterns")] public List<string> ExcludedPathPatterns { get; set; } = [];
}

public sealed class RunRequest
{
    [JsonPropertyName("discovery")] public DiscoveryRequest Discovery { get; set; } = new();
    [JsonPropertyName("tests")] public TestRunRequest Tests { get; set; } = new();
}

public sealed class RunDocument
{
    [JsonPropertyName("runId")] public string RunId { get; set; } = "";
    [JsonPropertyName("projectId")] public string ProjectId { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("currentStep")] public string CurrentStep { get; set; } = "";
    [JsonPropertyName("progress")] public int Progress { get; set; }
    [JsonPropertyName("startedAt")] public DateTimeOffset StartedAt { get; set; }
    [JsonPropertyName("completedAt")] public DateTimeOffset? CompletedAt { get; set; }
    [JsonPropertyName("endpointCount")] public int EndpointCount { get; set; }
    [JsonPropertyName("resultCount")] public int ResultCount { get; set; }
    [JsonPropertyName("findingCount")] public int FindingCount { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
}

public sealed class TestResult
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("projectId")] public string ProjectId { get; set; } = "";
    [JsonPropertyName("testRunId")] public string TestRunId { get; set; } = "";
    [JsonPropertyName("endpointId")] public string EndpointId { get; set; } = "";
    [JsonPropertyName("bendType")] public string BendType { get; set; } = "";
    [JsonPropertyName("category")] public string Category { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("method")] public string Method { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("originalUrl")] public string OriginalUrl { get; set; } = "";
    [JsonPropertyName("mutation")] public Dictionary<string, object> Mutation { get; set; } = [];
    [JsonPropertyName("request")] public ResultRequest Request { get; set; } = new();
    [JsonPropertyName("result")] public HttpResult Result { get; set; } = new();
    [JsonPropertyName("resultBody")] public string ResultBody { get; set; } = "";
    [JsonPropertyName("evidence")] public string Evidence { get; set; } = "";
    [JsonPropertyName("outcome")] public string Outcome { get; set; } = "";
    [JsonPropertyName("interesting")] public bool Interesting { get; set; }
    [JsonPropertyName("analysisSummary")] public string AnalysisSummary { get; set; } = "";
    [JsonPropertyName("owaspCategory")] public string OwaspCategory { get; set; } = "";
    [JsonPropertyName("recommendation")] public string Recommendation { get; set; } = "";
    [JsonPropertyName("risk")] public int Risk { get; set; }
    [JsonPropertyName("severity")] public string Severity { get; set; } = "";
    [JsonPropertyName("confidence")] public int Confidence { get; set; }
    [JsonPropertyName("reproducible")] public bool Reproducible { get; set; }
    [JsonPropertyName("sensitiveDataDetected")] public bool SensitiveDataDetected { get; set; }
    [JsonPropertyName("tokenUsed")] public bool TokenUsed { get; set; }
    [JsonPropertyName("authContext")] public AuthConfig AuthContext { get; set; } = new();
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ResultRequest
{
    [JsonPropertyName("headersMasked")] public Dictionary<string, string> HeadersMasked { get; set; } = [];
    [JsonPropertyName("body")] public string? Body { get; set; }
    [JsonPropertyName("bodySizeBytes")] public int BodySizeBytes { get; set; }
}

public sealed class HttpResult
{
    [JsonPropertyName("statusCode")] public int StatusCode { get; set; }
    [JsonPropertyName("statusText")] public string StatusText { get; set; } = "";
    [JsonPropertyName("headersMasked")] public Dictionary<string, string> HeadersMasked { get; set; } = [];
    [JsonPropertyName("contentType")] public string ContentType { get; set; } = "";
    [JsonPropertyName("bodySizeBytes")] public int BodySizeBytes { get; set; }
    [JsonPropertyName("durationMs")] public int DurationMs { get; set; }
}

public sealed class ResultsDocument
{
    [JsonPropertyName("generatedAt")] public DateTimeOffset GeneratedAt { get; set; }
    [JsonPropertyName("projectId")] public string ProjectId { get; set; } = "";
    [JsonPropertyName("results")] public List<TestResult> Results { get; set; } = [];
}
