namespace BendIt.Api.Discovery;

public sealed partial class DiscoveryOrchestrator
{
    private static (string Classification, int Confidence) ClassifyDiscoveryResult(DiscoveryHttpResult result, string source, BaselineFingerprint baseline, bool fromOpenApi)
    {
        if (result.Error is not null)
        {
            return (ClassUnknown, 10);
        }

        if (LooksLikeSoft404(result, baseline))
        {
            return (ClassSoft404, 20);
        }

        return result.StatusCode switch
        {
            >= 200 and <= 204 when fromOpenApi => (ClassConfirmed, 100),
            >= 200 and <= 204 when result.ContentType.Contains("json", StringComparison.OrdinalIgnoreCase) => (ClassConfirmed, 80),
            >= 200 and <= 204 when source == "openApiSpecProbe" => (ClassLikelyExists, 75),
            >= 200 and <= 204 => (ClassLikelyExists, 65),
            >= 300 and < 400 => (ClassLikelyExists, 55),
            400 => (ClassMaybeExists, 40),
            401 or 403 => (ClassProtected, 70),
            404 => (ClassNotFound, 0),
            405 => (ClassMethodNotAllowed, 65),
            429 => (ClassRateLimited, 30),
            >= 500 => (ClassServerError, 30),
            _ => (ClassUnknown, 15)
        };
    }

    private static bool ProjectlessAuthRequired(string classification) => classification == ClassProtected;
}
