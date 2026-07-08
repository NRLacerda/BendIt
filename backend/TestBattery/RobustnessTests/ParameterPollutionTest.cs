using BendIt.Api.Models;
using EndpointModel = BendIt.Api.Models.Endpoint;

namespace BendIt.Api.TestBattery.RobustnessTests;

internal sealed class ParameterPollutionTest : RobustnessTest
{
    private const string BendTypeValue = "parameterPollution";
    private const string JsonBaselineBody = """{"id":"1","role":"user"}""";
    private const string JsonPollutedBody = """{"id":"1","id":"2","role":"user","role":"admin"}""";
    private const string FormPollutedBody = "id=1&id=2&role=user&role=admin";

    public string BendType => BendTypeValue;

    public bool AppliesTo(EndpointModel endpoint, TestRunRequest request)
    {
        return true;
    }

    public async Task<TestResult> RunAsync(RobustnessTestContext context, CancellationToken cancellationToken)
    {
        var baseUrl = RobustnessTestHelpers.EndpointUrl(context.Endpoint, BendType);
        var baselineBody = RobustnessTestHelpers.AllowsRequestBody(context.Endpoint.Method) ? JsonBaselineBody : null;
        var baseline = await RobustnessTestHelpers.ExecuteAsync(
            context.Client,
            context.Endpoint.Method,
            baseUrl,
            baselineBody,
            context.Project,
            BendType,
            cancellationToken);

        var probes = new List<ParameterPollutionProbe> { QueryProbe(context.Endpoint, baseUrl) };
        if (RobustnessTestHelpers.AllowsRequestBody(context.Endpoint.Method))
        {
            probes.Add(new ParameterPollutionProbe("duplicateFormBody", baseUrl, FormPollutedBody, "application/x-www-form-urlencoded", DuplicateParameterNames()));
            probes.Add(new ParameterPollutionProbe("duplicateJsonKeys", baseUrl, JsonPollutedBody, "application/json", DuplicateParameterNames()));
        }

        var observations = new List<ParameterPollutionObservation>(probes.Count);
        foreach (var probe in probes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var executed = await RobustnessTestHelpers.ExecuteAsync(
                context.Client,
                context.Endpoint.Method,
                probe.Url,
                probe.Body,
                context.Project,
                BendType,
                probe.ContentType,
                true,
                cancellationToken);
            observations.Add(ParameterPollutionObservation.From(probe, baseline, executed));
        }

        var representative = observations
            .OrderByDescending(observation => observation.Risk)
            .ThenByDescending(observation => observation.Executed.StatusCode)
            .First();
        var risk = representative.Risk;
        var privileged = observations.Any(observation => observation.Signals.Any(IsPrivilegedSignal));
        var requestHeaders = RobustnessTestHelpers.MaskedHeaders(context.Project);
        if (!string.IsNullOrWhiteSpace(representative.Probe.ContentType))
        {
            requestHeaders["Content-Type"] = representative.Probe.ContentType;
        }

        return RobustnessResultFactory.Create(
            context,
            BendType,
            representative.Probe.Url,
            representative.Probe.Body,
            representative.Executed,
            risk,
            Mutation(observations, baseline),
            Evidence(context.Endpoint, observations, representative),
            AnalysisSummary(risk, representative),
            requestHeaders,
            false,
            $"parameterPollution observed HTTP {representative.Executed.StatusCode}",
            privileged ? "Authorization" : RobustnessTestHelpers.CategoryFor(BendType),
            privileged ? "API3: Broken Object Property Level Authorization" : RobustnessTestHelpers.OwaspCategoryFor(BendType));
    }

    private static ParameterPollutionProbe QueryProbe(EndpointModel endpoint, string baseUrl)
    {
        var parameterNames = PollutionParameterNames(endpoint).ToList();
        var parameters = parameterNames.SelectMany(name => ValuesFor(name).Select(value => new KeyValuePair<string, string>(name, value)));
        return new ParameterPollutionProbe("duplicateQuery", RobustnessTestHelpers.AppendQuery(baseUrl, parameters), null, null, parameterNames);
    }

    private static IEnumerable<string> PollutionParameterNames(EndpointModel endpoint)
    {
        var discovered = endpoint.QueryParams
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();
        return discovered.Count > 0 ? discovered : DuplicateParameterNames();
    }

    private static List<string> DuplicateParameterNames()
    {
        return ["id", "role"];
    }

    private static string[] ValuesFor(string name)
    {
        return name.Contains("role", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("admin", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("permission", StringComparison.OrdinalIgnoreCase)
            ? ["user", "admin"]
            : ["1", "2"];
    }

    private static Dictionary<string, object> Mutation(List<ParameterPollutionObservation> observations, ExecutedRequest baseline)
    {
        return new Dictionary<string, object>
        {
            ["type"] = "parameterPollution",
            ["baselineStatus"] = baseline.StatusCode,
            ["baselineBodyHash"] = RobustnessTestHelpers.ShortHash(baseline.Body),
            ["probes"] = observations.Select(observation => new Dictionary<string, object>
            {
                ["name"] = observation.Probe.Name,
                ["status"] = observation.Executed.StatusCode,
                ["risk"] = observation.Risk,
                ["duplicateParameters"] = observation.Probe.DuplicateParameters,
                ["signals"] = observation.Signals
            }).ToList()
        };
    }

    private static string Evidence(EndpointModel endpoint, List<ParameterPollutionObservation> observations, ParameterPollutionObservation representative)
    {
        if (representative.Risk < 5)
        {
            return $"{endpoint.Method} {endpoint.Path} rejected or ignored duplicated parameters across {observations.Count} probe(s).";
        }

        return $"{endpoint.Method} {endpoint.Path} showed parameter pollution risk during {representative.Probe.Name}; duplicate parameters: {string.Join(", ", representative.Probe.DuplicateParameters)}; signals: {string.Join(", ", representative.Signals)}.";
    }

    private static string AnalysisSummary(int risk, ParameterPollutionObservation representative)
    {
        if (risk >= 7)
        {
            return $"parameterPollution found that duplicated parameters can select privileged or alternate values during {representative.Probe.Name}.";
        }

        return risk >= 5
            ? $"parameterPollution found ambiguous duplicate-parameter handling during {representative.Probe.Name}."
            : "parameterPollution did not find accepted duplicate parameters with security-sensitive effects.";
    }

    private static List<string> Signals(ExecutedRequest baseline, ExecutedRequest executed)
    {
        var signals = new List<string>();
        if (executed.StatusCode >= 500)
        {
            signals.Add("server error on duplicate parameters");
        }

        if (IsAccepted(executed.StatusCode))
        {
            if (HasPrivilegedOverride(executed.Body))
            {
                signals.Add("privileged value accepted");
            }

            if (HasIdentifierOverride(executed.Body))
            {
                signals.Add("alternate identifier accepted");
            }

            if (!string.Equals(RobustnessTestHelpers.ShortHash(baseline.Body), RobustnessTestHelpers.ShortHash(executed.Body), StringComparison.Ordinal) &&
                HasPollutionMarker(executed.Body))
            {
                signals.Add("response changed with polluted value");
            }
        }

        return signals.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static int Risk(ExecutedRequest executed, List<string> signals)
    {
        if (signals.Any(IsPrivilegedSignal)) return 8;
        if (signals.Count > 0 && IsAccepted(executed.StatusCode)) return 5;
        if (executed.StatusCode >= 500) return 6;
        return 2;
    }

    private static bool IsAccepted(int status)
    {
        return status is >= 200 and < 300;
    }

    private static bool IsPrivilegedSignal(string signal)
    {
        return signal.Contains("privileged", StringComparison.OrdinalIgnoreCase) ||
               signal.Contains("identifier", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasPrivilegedOverride(string body)
    {
        return ContainsAny(body, "\"role\":\"admin\"", "\"role\": \"admin\"", "role=admin", "\"isAdmin\":true", "\"isAdmin\": true", "\"admin\":true", "\"admin\": true");
    }

    private static bool HasIdentifierOverride(string body)
    {
        return ContainsAny(body, "\"selectedId\":\"2\"", "\"selectedId\": \"2\"", "\"id\":\"2\"", "\"id\": \"2\"", "selectedId=2", "\"ownerId\":\"2\"", "\"tenantId\":\"2\"");
    }

    private static bool HasPollutionMarker(string body)
    {
        return ContainsAny(body, "admin", "selectedId", "lastValue", "winner", "duplicate", "polluted", "\"id\":\"2\"", "\"id\": \"2\"");
    }

    private static bool ContainsAny(string body, params string[] terms)
    {
        return !string.IsNullOrWhiteSpace(body) && terms.Any(term => body.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record ParameterPollutionProbe(string Name, string Url, string? Body, string? ContentType, List<string> DuplicateParameters);

    private sealed class ParameterPollutionObservation
    {
        public required ParameterPollutionProbe Probe { get; init; }
        public required ExecutedRequest Executed { get; init; }
        public required List<string> Signals { get; init; }
        public required int Risk { get; init; }

        public static ParameterPollutionObservation From(ParameterPollutionProbe probe, ExecutedRequest baseline, ExecutedRequest executed)
        {
            var signals = ParameterPollutionTest.Signals(baseline, executed);
            return new ParameterPollutionObservation
            {
                Probe = probe,
                Executed = executed,
                Signals = signals,
                Risk = ParameterPollutionTest.Risk(executed, signals)
            };
        }
    }
}
