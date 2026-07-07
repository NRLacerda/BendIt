using BendIt.Api.Models;
using EndpointModel = BendIt.Api.Models.Endpoint;

namespace BendIt.Api.TestBattery.RobustnessTests;

internal sealed class ContentTypeValidationTest : RobustnessTest
{
    private const string Body = """{"payload":"content-type-check"}""";

    public string BendType => "contentTypeValidation";

    public bool AppliesTo(EndpointModel endpoint, TestRunRequest request)
    {
        return RobustnessTestHelpers.AllowsRequestBody(endpoint.Method);
    }

    public async Task<TestResult> RunAsync(RobustnessTestContext context, CancellationToken cancellationToken)
    {
        var url = RobustnessTestHelpers.EndpointUrl(context.Endpoint, BendType);
        var baseline = await RobustnessTestHelpers.ExecuteAsync(
            context.Client,
            context.Endpoint.Method,
            url,
            Body,
            context.Project,
            BendType,
            "application/json",
            true,
            cancellationToken);
        var textPlain = await RobustnessTestHelpers.ExecuteAsync(
            context.Client,
            context.Endpoint.Method,
            url,
            Body,
            context.Project,
            BendType,
            "text/plain",
            true,
            cancellationToken);
        var missing = await RobustnessTestHelpers.ExecuteAsync(
            context.Client,
            context.Endpoint.Method,
            url,
            Body,
            context.Project,
            BendType,
            null,
            false,
            cancellationToken);

        var observation = ContentTypeObservation.From(baseline, textPlain, missing);
        var representative = observation.Representative;
        var risk = Risk(observation);
        var interesting = RobustnessTestHelpers.IsInteresting(BendType, representative.StatusCode, risk);
        var now = DateTimeOffset.UtcNow;

        return new TestResult
        {
            Id = "result_" + RobustnessTestHelpers.ShortHash(context.Endpoint.Id + BendType + now.ToString("O")),
            ProjectId = context.Project.ProjectId,
            TestRunId = context.TestRunId,
            EndpointId = context.Endpoint.Id,
            BendType = BendType,
            Category = RobustnessTestHelpers.CategoryFor(BendType),
            Title = $"contentTypeValidation observed HTTP {representative.StatusCode}",
            Method = context.Endpoint.Method,
            Url = url,
            OriginalUrl = url,
            Mutation = Mutation(observation),
            Request = new ResultRequest
            {
                HeadersMasked = RequestHeaders(context.Project, observation.RepresentativeVariant),
                Body = Body,
                BodySizeBytes = Body.Length
            },
            Result = new HttpResult
            {
                StatusCode = representative.StatusCode,
                StatusText = representative.StatusText,
                HeadersMasked = RobustnessTestHelpers.MaskResponseHeaders(representative.ResponseHeaders),
                ContentType = representative.ContentType,
                BodySizeBytes = representative.BodySizeBytes,
                DurationMs = representative.DurationMs
            },
            ResultBody = representative.Body,
            Evidence = Evidence(context.Endpoint, observation),
            Outcome = RobustnessTestHelpers.OutcomeFor(BendType, representative.StatusCode, risk),
            Interesting = interesting,
            AnalysisSummary = AnalysisSummary(risk, observation),
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

    private static int Risk(ContentTypeObservation observation)
    {
        if (!IsAccepted(observation.Baseline.StatusCode))
        {
            return 2;
        }

        var acceptedInvalidVariants = observation.AcceptedInvalidVariants.Count;
        return acceptedInvalidVariants switch
        {
            >= 2 => 7,
            1 => 5,
            _ => 2
        };
    }

    private static Dictionary<string, object> Mutation(ContentTypeObservation observation)
    {
        return new Dictionary<string, object>
        {
            ["type"] = "contentTypeEnforcement",
            ["baselineContentType"] = "application/json",
            ["baselineStatus"] = observation.Baseline.StatusCode,
            ["textPlainStatus"] = observation.TextPlain.StatusCode,
            ["missingContentTypeStatus"] = observation.MissingContentType.StatusCode,
            ["acceptedInvalidVariants"] = observation.AcceptedInvalidVariants,
            ["rejectedInvalidVariants"] = observation.RejectedInvalidVariants,
            ["baselineAccepted"] = IsAccepted(observation.Baseline.StatusCode)
        };
    }

    private static string Evidence(EndpointModel endpoint, ContentTypeObservation observation)
    {
        if (!IsAccepted(observation.Baseline.StatusCode))
        {
            return $"{endpoint.Method} {endpoint.Path} did not accept baseline application/json content-type validation request; baseline HTTP {observation.Baseline.StatusCode}, text/plain HTTP {observation.TextPlain.StatusCode}, missing Content-Type HTTP {observation.MissingContentType.StatusCode}.";
        }

        var findings = new List<string>();
        findings.Add(IsAccepted(observation.TextPlain.StatusCode)
            ? $"text/plain JSON was accepted with HTTP {observation.TextPlain.StatusCode}"
            : $"text/plain JSON was rejected with HTTP {observation.TextPlain.StatusCode}");
        findings.Add(IsAccepted(observation.MissingContentType.StatusCode)
            ? $"missing Content-Type JSON was accepted with HTTP {observation.MissingContentType.StatusCode}"
            : $"missing Content-Type JSON was rejected with HTTP {observation.MissingContentType.StatusCode}");

        return $"{endpoint.Method} {endpoint.Path} accepted baseline application/json with HTTP {observation.Baseline.StatusCode}; " + string.Join("; ", findings) + ".";
    }

    private static string AnalysisSummary(int risk, ContentTypeObservation observation)
    {
        if (!IsAccepted(observation.Baseline.StatusCode))
        {
            return "contentTypeValidation could not establish a successful application/json baseline, so content-type enforcement is inconclusive.";
        }

        return risk >= 5
            ? "contentTypeValidation found that the endpoint accepted JSON with an ambiguous or wrong Content-Type."
            : "contentTypeValidation observed rejection of ambiguous and wrong Content-Type variants.";
    }

    private static Dictionary<string, string> RequestHeaders(Project project, string representativeVariant)
    {
        var headers = RobustnessTestHelpers.MaskedHeaders(project);
        headers["Content-Type"] = representativeVariant switch
        {
            "textPlain" => "text/plain",
            "missingContentType" => "<missing>",
            _ => "application/json"
        };
        return headers;
    }

    private static bool IsAccepted(int status)
    {
        return status is >= 200 and < 300;
    }

    private static bool IsExpectedRejection(int status)
    {
        return status is 400 or 415 or 422;
    }

    private sealed class ContentTypeObservation
    {
        public required ExecutedRequest Baseline { get; init; }
        public required ExecutedRequest TextPlain { get; init; }
        public required ExecutedRequest MissingContentType { get; init; }
        public required List<string> AcceptedInvalidVariants { get; init; }
        public required List<string> RejectedInvalidVariants { get; init; }
        public required ExecutedRequest Representative { get; init; }
        public required string RepresentativeVariant { get; init; }

        public static ContentTypeObservation From(ExecutedRequest baseline, ExecutedRequest textPlain, ExecutedRequest missingContentType)
        {
            var accepted = new List<string>();
            var rejected = new List<string>();

            AddVariant(accepted, rejected, "text/plain", textPlain.StatusCode);
            AddVariant(accepted, rejected, "missing Content-Type", missingContentType.StatusCode);

            var (representative, representativeVariant) = IsAccepted(textPlain.StatusCode)
                ? (textPlain, "textPlain")
                : IsAccepted(missingContentType.StatusCode)
                    ? (missingContentType, "missingContentType")
                    : (textPlain, "textPlain");

            return new ContentTypeObservation
            {
                Baseline = baseline,
                TextPlain = textPlain,
                MissingContentType = missingContentType,
                AcceptedInvalidVariants = accepted,
                RejectedInvalidVariants = rejected,
                Representative = representative,
                RepresentativeVariant = representativeVariant
            };
        }

        private static void AddVariant(List<string> accepted, List<string> rejected, string name, int status)
        {
            if (IsAccepted(status))
            {
                accepted.Add(name);
            }
            else if (IsExpectedRejection(status))
            {
                rejected.Add(name);
            }
        }
    }
}
