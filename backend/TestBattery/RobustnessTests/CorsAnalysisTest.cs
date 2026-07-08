using BendIt.Api.Models;
using EndpointModel = BendIt.Api.Models.Endpoint;

namespace BendIt.Api.TestBattery.RobustnessTests;

internal sealed class CorsAnalysisTest : RobustnessTest
{
    private const string BendTypeValue = "corsAnalysis";
    private const string AttackerOrigin = "https://evil.example";
    private const string SecondAttackerOrigin = "https://attacker.invalid";
    private const string RequestedHeaders = "Authorization, Content-Type, X-API-Key";

    public string BendType => BendTypeValue;

    public bool AppliesTo(EndpointModel endpoint, TestRunRequest request)
    {
        return true;
    }

    public async Task<TestResult> RunAsync(RobustnessTestContext context, CancellationToken cancellationToken)
    {
        var url = RobustnessTestHelpers.EndpointUrl(context.Endpoint, BendType);
        var probes = new[]
        {
            new CorsProbe("actualAttackerOrigin", context.Endpoint.Method, AttackerOrigin, null, null),
            new CorsProbe("actualSecondOrigin", context.Endpoint.Method, SecondAttackerOrigin, null, null),
            new CorsProbe("preflightAttackerOrigin", "OPTIONS", AttackerOrigin, context.Endpoint.Method, RequestedHeaders)
        };

        var observations = new List<CorsObservation>(probes.Length);
        foreach (var probe in probes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var executed = await RobustnessTestHelpers.ExecuteAsync(
                context.Client,
                probe.Method,
                url,
                null,
                context.Project,
                BendType,
                "application/json",
                false,
                ProbeHeaders(probe),
                [],
                cancellationToken);
            observations.Add(CorsObservation.From(probe, executed));
        }

        var reflected = ReflectsArbitraryOrigins(observations);
        foreach (var observation in observations)
        {
            observation.ApplyReflectionSignal(reflected);
        }

        var representative = observations
            .OrderByDescending(observation => observation.Risk)
            .ThenByDescending(observation => observation.Executed.StatusCode)
            .First();
        var risk = representative.Risk;
        var requestHeaders = RobustnessTestHelpers.MaskedHeaders(context.Project);
        requestHeaders["Origin"] = representative.Probe.Origin;
        if (!string.IsNullOrWhiteSpace(representative.Probe.RequestMethod))
        {
            requestHeaders["Access-Control-Request-Method"] = representative.Probe.RequestMethod;
        }

        if (!string.IsNullOrWhiteSpace(representative.Probe.RequestHeaders))
        {
            requestHeaders["Access-Control-Request-Headers"] = representative.Probe.RequestHeaders;
        }

        return RobustnessResultFactory.Create(
            context,
            BendType,
            url,
            null,
            representative.Executed,
            risk,
            Mutation(observations),
            Evidence(context.Endpoint, observations, representative),
            AnalysisSummary(risk, representative),
            requestHeaders,
            false,
            $"corsAnalysis observed HTTP {representative.Executed.StatusCode}");
    }

    private static IEnumerable<KeyValuePair<string, string>> ProbeHeaders(CorsProbe probe)
    {
        yield return new KeyValuePair<string, string>("Origin", probe.Origin);
        if (!string.IsNullOrWhiteSpace(probe.RequestMethod))
        {
            yield return new KeyValuePair<string, string>("Access-Control-Request-Method", probe.RequestMethod);
        }

        if (!string.IsNullOrWhiteSpace(probe.RequestHeaders))
        {
            yield return new KeyValuePair<string, string>("Access-Control-Request-Headers", probe.RequestHeaders);
        }
    }

    private static bool ReflectsArbitraryOrigins(List<CorsObservation> observations)
    {
        var first = observations.FirstOrDefault(observation => observation.Probe.Name == "actualAttackerOrigin");
        var second = observations.FirstOrDefault(observation => observation.Probe.Name == "actualSecondOrigin");
        return first?.AllowOrigin.Equals(AttackerOrigin, StringComparison.OrdinalIgnoreCase) == true &&
               second?.AllowOrigin.Equals(SecondAttackerOrigin, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static Dictionary<string, object> Mutation(List<CorsObservation> observations)
    {
        return new Dictionary<string, object>
        {
            ["type"] = "corsAnalysis",
            ["probes"] = observations.Select(observation => new Dictionary<string, object>
            {
                ["name"] = observation.Probe.Name,
                ["origin"] = observation.Probe.Origin,
                ["method"] = observation.Probe.Method,
                ["status"] = observation.Executed.StatusCode,
                ["allowOrigin"] = observation.AllowOrigin,
                ["allowCredentials"] = observation.AllowCredentials,
                ["allowMethods"] = observation.AllowMethods,
                ["allowHeaders"] = observation.AllowHeaders,
                ["signals"] = observation.Signals,
                ["risk"] = observation.Risk
            }).ToList()
        };
    }

    private static string Evidence(EndpointModel endpoint, List<CorsObservation> observations, CorsObservation representative)
    {
        if (representative.Risk < 5)
        {
            return $"{endpoint.Method} {endpoint.Path} did not expose dangerous CORS policy to attacker origins across {observations.Count} probe(s).";
        }

        return $"{endpoint.Method} {endpoint.Path} exposed risky CORS behavior during {representative.Probe.Name}; origin {representative.Probe.Origin}; signals: {string.Join(", ", representative.Signals)}.";
    }

    private static string AnalysisSummary(int risk, CorsObservation representative)
    {
        return risk >= 5
            ? $"corsAnalysis found unsafe cross-origin policy behavior during {representative.Probe.Name}."
            : "corsAnalysis did not find wildcard, reflected-origin, credentialed, or overbroad preflight CORS exposure.";
    }

    private static bool IsSensitiveHeaderAllowed(string allowHeaders)
    {
        return ContainsToken(allowHeaders, "*") ||
               ContainsToken(allowHeaders, "authorization") ||
               ContainsToken(allowHeaders, "cookie") ||
               ContainsToken(allowHeaders, "x-api-key");
    }

    private static bool AllowsBroadMethods(string allowMethods)
    {
        return ContainsToken(allowMethods, "*") ||
               ContainsToken(allowMethods, "delete") ||
               ContainsToken(allowMethods, "put") ||
               ContainsToken(allowMethods, "patch");
    }

    private static bool ContainsToken(string value, string token)
    {
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(item => string.Equals(item, token, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record CorsProbe(string Name, string Method, string Origin, string? RequestMethod, string? RequestHeaders);

    private sealed class CorsObservation
    {
        public required CorsProbe Probe { get; init; }
        public required ExecutedRequest Executed { get; init; }
        public required string AllowOrigin { get; init; }
        public required string AllowMethods { get; init; }
        public required string AllowHeaders { get; init; }
        public required bool AllowCredentials { get; init; }
        public required List<string> Signals { get; init; }
        public int Risk { get; private set; }

        public static CorsObservation From(CorsProbe probe, ExecutedRequest executed)
        {
            var allowOrigin = Header(executed, "Access-Control-Allow-Origin");
            var allowMethods = Header(executed, "Access-Control-Allow-Methods");
            var allowHeaders = Header(executed, "Access-Control-Allow-Headers");
            var allowCredentials = string.Equals(Header(executed, "Access-Control-Allow-Credentials"), "true", StringComparison.OrdinalIgnoreCase);
            var signals = SignalsFor(probe, allowOrigin, allowMethods, allowHeaders, allowCredentials);
            return new CorsObservation
            {
                Probe = probe,
                Executed = executed,
                AllowOrigin = allowOrigin,
                AllowMethods = allowMethods,
                AllowHeaders = allowHeaders,
                AllowCredentials = allowCredentials,
                Signals = signals,
                Risk = RiskFor(signals)
            };
        }

        public void ApplyReflectionSignal(bool reflected)
        {
            if (reflected && AllowOrigin.Equals(Probe.Origin, StringComparison.OrdinalIgnoreCase))
            {
                AddSignal("reflected arbitrary origin");
                Risk = RiskFor(Signals);
            }
        }

        private static List<string> SignalsFor(CorsProbe probe, string allowOrigin, string allowMethods, string allowHeaders, bool allowCredentials)
        {
            var signals = new List<string>();
            if (string.IsNullOrWhiteSpace(allowOrigin))
            {
                return signals;
            }

            if (allowOrigin == "*")
            {
                signals.Add("wildcard origin allowed");
            }

            if (allowCredentials && (allowOrigin == "*" || allowOrigin.Equals(probe.Origin, StringComparison.OrdinalIgnoreCase)))
            {
                signals.Add("credentialed cross-origin access allowed");
            }

            if (probe.Name.StartsWith("preflight", StringComparison.OrdinalIgnoreCase))
            {
                if (IsSensitiveHeaderAllowed(allowHeaders))
                {
                    signals.Add("sensitive request headers allowed by preflight");
                }

                if (AllowsBroadMethods(allowMethods))
                {
                    signals.Add("broad methods allowed by preflight");
                }
            }

            return signals;
        }

        private void AddSignal(string signal)
        {
            if (!Signals.Contains(signal, StringComparer.OrdinalIgnoreCase))
            {
                Signals.Add(signal);
            }
        }

        private static int RiskFor(List<string> signals)
        {
            if (signals.Any(signal => signal.Contains("credentialed", StringComparison.OrdinalIgnoreCase) || signal.Contains("reflected", StringComparison.OrdinalIgnoreCase))) return 8;
            if (signals.Any(signal => signal.Contains("sensitive", StringComparison.OrdinalIgnoreCase) || signal.Contains("broad", StringComparison.OrdinalIgnoreCase))) return 6;
            if (signals.Any(signal => signal.Contains("wildcard", StringComparison.OrdinalIgnoreCase))) return 5;
            return 2;
        }

        private static string Header(ExecutedRequest executed, string name)
        {
            foreach (var header in executed.ResponseHeaders)
            {
                if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    return header.Value;
                }
            }

            return "";
        }
    }
}
