using BendIt.Api.Models;
using EndpointModel = BendIt.Api.Models.Endpoint;

namespace BendIt.Api.TestBattery.RobustnessTests;

internal sealed class ErrorDisclosureTest : RobustnessTest
{
    private static readonly ErrorDisclosureProbe[] Probes =
    [
        new("malformedJson", """{"payload":"""),
        new("emptyObject", "{}"),
        new("wrongTypes", """{"id":{},"email":123,"amount":"not-a-number","createdAt":"not-a-date"}"""),
        new("internalFieldProbe", """{"tenantId":"bendit-probe","ownerId":"bendit-probe","isAdmin":true,"role":"admin"}""")
    ];

    private static readonly DisclosurePattern[] Patterns =
    [
        new("exception", 9, ["NullReferenceException", "InvalidOperationException", "ArgumentNullException", "SqlException", "NpgsqlException", "DbUpdateException", "TimeoutException", "TypeError", "ReferenceError"]),
        new("stack trace", 9, ["StackTrace", "Traceback", ".cs:line", ".java:", ".py", " at "]),
        new("file path", 9, ["C:\\", "/var/www", "/usr/src", "/app/", "\\Controllers\\", "\\Services\\"]),
        new("database or connection detail", 7, ["SQL Server", "PostgreSQL", "MySQL", "connection pool", "deadlock", "timeout expired", "ECONNREFUSED"]),
        new("internal dependency", 7, ["upstream", "internal service", "service bus", "redis", "rabbitmq", "kafka"]),
        new("framework or runtime", 7, ["ASP.NET Core", "Kestrel", "Entity Framework", "Hibernate", "Spring", "Express", "Django", "Laravel"]),
        new("internal field hint", 5, ["tenantId", "ownerId", "accountId", "internalId", "isAdmin", "permissions", "claims"]),
        new("verbose model binding", 5, ["ModelState", "DTO", "ViewModel", "property path", "$.", "entity"])
    ];

    public string BendType => "errorDisclosure";

    public bool AppliesTo(EndpointModel endpoint, TestRunRequest request)
    {
        return RobustnessTestHelpers.AllowsRequestBody(endpoint.Method);
    }

    public async Task<TestResult> RunAsync(RobustnessTestContext context, CancellationToken cancellationToken)
    {
        var url = RobustnessTestHelpers.EndpointUrl(context.Endpoint, BendType);
        var observations = new List<ErrorDisclosureObservation>(Probes.Length);
        foreach (var probe in Probes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var executed = await RobustnessTestHelpers.ExecuteAsync(
                context.Client,
                context.Endpoint.Method,
                url,
                probe.Body,
                context.Project,
                BendType,
                cancellationToken);
            observations.Add(ErrorDisclosureObservation.From(probe, executed));
        }

        var representative = observations
            .OrderByDescending(observation => observation.Risk)
            .ThenByDescending(observation => observation.Executed.StatusCode >= 500)
            .First();
        var risk = representative.Risk;
        var now = DateTimeOffset.UtcNow;
        var interesting = RobustnessTestHelpers.IsInteresting(BendType, representative.Executed.StatusCode, risk);

        return new TestResult
        {
            Id = "result_" + RobustnessTestHelpers.ShortHash(context.Endpoint.Id + BendType + now.ToString("O")),
            ProjectId = context.Project.ProjectId,
            TestRunId = context.TestRunId,
            EndpointId = context.Endpoint.Id,
            BendType = BendType,
            Category = RobustnessTestHelpers.CategoryFor(BendType),
            Title = $"errorDisclosure observed HTTP {representative.Executed.StatusCode}",
            Method = context.Endpoint.Method,
            Url = url,
            OriginalUrl = url,
            Mutation = Mutation(observations),
            Request = new ResultRequest
            {
                HeadersMasked = RobustnessTestHelpers.MaskedHeaders(context.Project),
                Body = representative.Probe.Body,
                BodySizeBytes = representative.Probe.Body.Length
            },
            Result = new HttpResult
            {
                StatusCode = representative.Executed.StatusCode,
                StatusText = representative.Executed.StatusText,
                HeadersMasked = RobustnessTestHelpers.MaskResponseHeaders(representative.Executed.ResponseHeaders),
                ContentType = representative.Executed.ContentType,
                BodySizeBytes = representative.Executed.BodySizeBytes,
                DurationMs = representative.Executed.DurationMs
            },
            ResultBody = representative.Executed.Body,
            Evidence = Evidence(context.Endpoint, observations, representative),
            Outcome = RobustnessTestHelpers.OutcomeFor(BendType, representative.Executed.StatusCode, risk),
            Interesting = interesting,
            AnalysisSummary = AnalysisSummary(risk, representative),
            OwaspCategory = RobustnessTestHelpers.OwaspCategoryFor(BendType),
            Recommendation = RobustnessTestHelpers.RecommendationFor(BendType, risk),
            Risk = risk,
            Severity = RobustnessTestHelpers.SeverityFor(risk),
            Confidence = Math.Min(96, 58 + risk * 4),
            Reproducible = risk >= 6,
            SensitiveDataDetected = false,
            TokenUsed = context.Project.Auth.Type != "none",
            AuthContext = context.Project.Auth,
            CreatedAt = now
        };
    }

    private static Dictionary<string, object> Mutation(List<ErrorDisclosureObservation> observations)
    {
        return new Dictionary<string, object>
        {
            ["type"] = "errorDisclosureProbe",
            ["probes"] = observations.Select(observation => new Dictionary<string, object>
            {
                ["name"] = observation.Probe.Name,
                ["status"] = observation.Executed.StatusCode,
                ["risk"] = observation.Risk,
                ["categories"] = observation.Matches.Select(match => match.Category).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                ["matchedTerms"] = observation.Matches.Select(match => match.Term).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList()
            }).ToList()
        };
    }

    private static string Evidence(EndpointModel endpoint, List<ErrorDisclosureObservation> observations, ErrorDisclosureObservation representative)
    {
        if (representative.Risk < 5)
        {
            return $"{endpoint.Method} {endpoint.Path} returned validation/parser errors without dangerous implementation disclosure across {observations.Count} probe(s).";
        }

        var categories = representative.Matches
            .Select(match => match.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var terms = representative.Matches
            .Select(match => match.Term)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();
        return $"{endpoint.Method} {endpoint.Path} exposed {string.Join(", ", categories)} during {representative.Probe.Name}; matched terms: {string.Join(", ", terms)}.";
    }

    private static string AnalysisSummary(int risk, ErrorDisclosureObservation representative)
    {
        return risk >= 5
            ? $"errorDisclosure found verbose error details during {representative.Probe.Name} and should be reviewed."
            : "errorDisclosure did not find stack traces, framework details, database errors, or internal field hints.";
    }

    private static List<DisclosureMatch> Matches(string body)
    {
        var matches = new List<DisclosureMatch>();
        if (string.IsNullOrWhiteSpace(body))
        {
            return matches;
        }

        foreach (var pattern in Patterns)
        {
            foreach (var term in pattern.Terms)
            {
                if (body.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(new DisclosureMatch(pattern.Category, term, pattern.Risk));
                }
            }
        }

        return matches;
    }

    private sealed record ErrorDisclosureProbe(string Name, string Body);
    private sealed record DisclosurePattern(string Category, int Risk, string[] Terms);
    private sealed record DisclosureMatch(string Category, string Term, int Risk);

    private sealed class ErrorDisclosureObservation
    {
        public required ErrorDisclosureProbe Probe { get; init; }
        public required ExecutedRequest Executed { get; init; }
        public required List<DisclosureMatch> Matches { get; init; }
        public int Risk => Matches.Count == 0 ? 2 : Matches.Max(match => match.Risk);

        public static ErrorDisclosureObservation From(ErrorDisclosureProbe probe, ExecutedRequest executed)
        {
            return new ErrorDisclosureObservation
            {
                Probe = probe,
                Executed = executed,
                Matches = ErrorDisclosureTest.Matches(executed.Body)
            };
        }
    }
}
