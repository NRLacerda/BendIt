using System.Text.RegularExpressions;
using BendIt.Api.Models;

namespace BendIt.Api.TestBattery;

internal sealed class ApiDownGuard(int threshold)
{
    private string? previousFingerprint;
    private int streak;

    public bool Enabled => threshold > 0;

    public TestResult? Observe(Project project, string testRunId, TestResult result)
    {
        if (!Enabled)
        {
            return null;
        }

        if (result.BendType == "dependencyResilience" &&
            result.Mutation.TryGetValue("failureStreak", out var rawStreak) &&
            Convert.ToInt32(rawStreak) >= threshold)
        {
            previousFingerprint = Fingerprint(result);
            streak = Convert.ToInt32(rawStreak);
            return CreateResult(project, testRunId, result, "dependencyResilience observed repeated matching dependency failures");
        }

        if (!IsFailureLike(result))
        {
            previousFingerprint = null;
            streak = 0;
            return null;
        }

        var fingerprint = Fingerprint(result);
        streak = fingerprint == previousFingerprint ? streak + 1 : 1;
        previousFingerprint = fingerprint;

        return streak >= threshold
            ? CreateResult(project, testRunId, result, "test battery observed repeated matching failure responses")
            : null;
    }

    public static bool IsFailureLike(TestResult result)
    {
        return IsFailureLike(result.Result.StatusCode, result.Result.ContentType, result.ResultBody, result.Evidence + " " + result.AnalysisSummary);
    }

    public static bool IsFailureLike(int statusCode, string contentType, string body, string? error)
    {
        if (statusCode == 0 || statusCode >= 500)
        {
            return true;
        }

        var text = $"{contentType} {body} {error}";
        return RobustnessTestHelpers.DependencyFailureSignals(text).Count > 0;
    }

    public static string Fingerprint(TestResult result)
    {
        return Fingerprint(result.Result.StatusCode, result.Result.ContentType, result.ResultBody + " " + result.Evidence + " " + result.AnalysisSummary, null);
    }

    public static string Fingerprint(ExecutedRequest request)
    {
        return Fingerprint(request.StatusCode, request.ContentType, request.Body, request.Error);
    }

    public static string Fingerprint(int statusCode, string contentType, string body, string? error)
    {
        var typeClass = string.IsNullOrWhiteSpace(contentType) ? "unknown" : contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
        var normalized = Normalize($"{body} {error}");
        return $"{statusCode}|{typeClass}|{RobustnessTestHelpers.ShortHash(normalized)}";
    }

    private TestResult CreateResult(Project project, string testRunId, TestResult trigger, string reason)
    {
        var now = DateTimeOffset.UtcNow;
        return new TestResult
        {
            Id = "result_" + RobustnessTestHelpers.ShortHash(project.ProjectId + "apiDownGuard" + now.ToString("O")),
            ProjectId = project.ProjectId,
            TestRunId = testRunId,
            EndpointId = trigger.EndpointId,
            BendType = "apiDownGuard",
            Category = RobustnessTestHelpers.CategoryFor("apiDownGuard"),
            Title = "apiDownGuard stopped the test battery",
            Method = trigger.Method,
            Url = trigger.Url,
            OriginalUrl = trigger.OriginalUrl,
            Mutation = new Dictionary<string, object>
            {
                ["type"] = "apiDownGuard",
                ["threshold"] = threshold,
                ["streak"] = streak,
                ["triggerResultId"] = trigger.Id,
                ["triggerBendType"] = trigger.BendType,
                ["fingerprint"] = previousFingerprint ?? "",
                ["stoppedRemainingTests"] = true
            },
            Request = trigger.Request,
            Result = trigger.Result,
            ResultBody = trigger.ResultBody,
            Evidence = $"Run stopped after {streak} repeated matching failure response(s): {reason}. Last endpoint: {trigger.Method} {trigger.Url}.",
            Outcome = "finding",
            Interesting = true,
            AnalysisSummary = "apiDownGuard determined the target API is likely down or degraded and stopped remaining tests to avoid wasting requests.",
            OwaspCategory = RobustnessTestHelpers.OwaspCategoryFor("apiDownGuard"),
            Recommendation = RobustnessTestHelpers.RecommendationFor("apiDownGuard", 8),
            Risk = 8,
            Severity = RobustnessTestHelpers.SeverityFor(8),
            Confidence = 90,
            Reproducible = true,
            SensitiveDataDetected = false,
            TokenUsed = project.Auth.Type != "none",
            AuthContext = project.Auth,
            CreatedAt = now
        };
    }

    private static string Normalize(string value)
    {
        var normalized = value.ToLowerInvariant();
        normalized = Regex.Replace(normalized, @"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", "<uuid>", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\d{4}-\d{2}-\d{2}[t\s]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:z|[+-]\d{2}:?\d{2})?", "<timestamp>", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\b\d+(?:\.\d+)?\b", "<num>");
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        return normalized;
    }
}
