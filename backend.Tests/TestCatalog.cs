using BendIt.Api.Tests.Discovery;
using BendIt.Api.Tests.TestBattery;

namespace BendIt.Api.Tests;

internal static class TestCatalog
{
    public static async Task RunAllAsync()
    {
        var tests = new List<(string Name, Func<Task> Run)>
        {
            ("native api list contains api routes from go implementation", DiscoveryTests.NativeApiListContainsApiRoutes),
            ("discovery and battery produce results for local api", DiscoveryTests.DiscoveryAndBatteryProduceResultsForLocalApi),
            ("likely exists health endpoint still produces result evidence", DiscoveryTests.LikelyExistsHealthEndpointProducesResultEvidence),
            ("localhost https discovery falls back to http", DiscoveryTests.LocalhostHttpsDiscoveryFallsBackToHttp),
            ("test battery caps response body capture", TestBatteryCoreTests.TestBatteryCapsResponseBodyCapture),
            ("test battery routes body tests by http verb", TestBatteryCoreTests.TestBatteryRoutesBodyTestsByHttpVerb),
            ("test battery maps results to OWASP categories", TestBatteryCoreTests.TestBatteryMapsResultsToOwaspCategories),
            ("http method validator accepts strict method enforcement", TestBatteryCoreTests.HttpMethodValidatorAcceptsStrictMethodEnforcement),
            ("http method validator flags alternate method acceptance", TestBatteryCoreTests.HttpMethodValidatorFlagsAlternateMethodAcceptance),
            ("response diffing accepts stable responses", TestBatteryCoreTests.ResponseDiffingAcceptsStableResponses),
            ("response diffing flags changed response bodies", TestBatteryCoreTests.ResponseDiffingFlagsChangedResponseBodies),
            ("dependency resilience accepts stable dependency", DependencyResilienceValidatorTests.DependencyResilienceAcceptsStableDependency),
            ("dependency resilience flags mongo timeout", DependencyResilienceValidatorTests.DependencyResilienceFlagsMongoTimeout),
            ("api down guard stops after repeated matching failures", DependencyResilienceValidatorTests.ApiDownGuardStopsAfterRepeatedMatchingFailures),
            ("api down guard does not stop below threshold", DependencyResilienceValidatorTests.ApiDownGuardDoesNotStopBelowThreshold),
            ("api down guard does not stop different fingerprints", DependencyResilienceValidatorTests.ApiDownGuardDoesNotStopDifferentFingerprints),
            ("api down guard can be disabled", DependencyResilienceValidatorTests.ApiDownGuardCanBeDisabled),
            ("security headers validator records misconfiguration evidence", SecurityValidatorTests.SecurityHeadersValidatorRecordsMisconfigurationEvidence),
            ("auth boundary validator sends malformed jwt probes", SecurityValidatorTests.AuthBoundaryValidatorSendsMalformedJwtProbes),
            ("id mutation detects query identifier parameters", SecurityValidatorTests.IdMutationDetectsQueryIdentifierParameters),
            ("inventory validator flags live legacy routes", SecurityValidatorTests.InventoryValidatorFlagsLiveLegacyRoutes),
            ("sensitive data validator reports data classes without values", SecurityValidatorTests.SensitiveDataValidatorReportsDataClassesWithoutValues),
            ("rate limit validator detects throttled burst", RateLimitValidatorTests.RateLimitValidatorDetectsThrottledBurst),
            ("rate limit validator detects missing throttling", RateLimitValidatorTests.RateLimitValidatorDetectsMissingThrottling),
            ("rate limit validator detects degraded post-burst response", RateLimitValidatorTests.RateLimitValidatorDetectsDegradedPostBurstResponse),
            ("rate limit validator caps requested burst size", RateLimitValidatorTests.RateLimitValidatorCapsRequestedBurstSize),
            ("content type validator accepts secure json only endpoint", ContentTypeValidatorTests.ContentTypeValidatorAcceptsSecureJsonOnlyEndpoint),
            ("content type validator flags text plain json acceptance", ContentTypeValidatorTests.ContentTypeValidatorFlagsTextPlainJsonAcceptance),
            ("content type validator flags missing content type acceptance", ContentTypeValidatorTests.ContentTypeValidatorFlagsMissingContentTypeAcceptance),
            ("content type validator skips get endpoints", ContentTypeValidatorTests.ContentTypeValidatorSkipsGetEndpoints),
            ("error disclosure validator accepts sanitized validation errors", ErrorDisclosureValidatorTests.ErrorDisclosureValidatorAcceptsSanitizedValidationErrors),
            ("error disclosure validator flags internal field hints", ErrorDisclosureValidatorTests.ErrorDisclosureValidatorFlagsInternalFieldHints),
            ("error disclosure validator flags stack traces", ErrorDisclosureValidatorTests.ErrorDisclosureValidatorFlagsStackTraces),
            ("error disclosure validator flags database connection leaks", ErrorDisclosureValidatorTests.ErrorDisclosureValidatorFlagsDatabaseConnectionLeaks),
            ("error disclosure validator skips get endpoints", ErrorDisclosureValidatorTests.ErrorDisclosureValidatorSkipsGetEndpoints),
            ("parameter pollution validator accepts strict query rejection", ParameterPollutionValidatorTests.ParameterPollutionValidatorAcceptsStrictQueryRejection),
            ("parameter pollution validator flags query override", ParameterPollutionValidatorTests.ParameterPollutionValidatorFlagsQueryOverride),
            ("parameter pollution validator accepts strict body rejection", ParameterPollutionValidatorTests.ParameterPollutionValidatorAcceptsStrictBodyRejection),
            ("parameter pollution validator flags body override", ParameterPollutionValidatorTests.ParameterPollutionValidatorFlagsBodyOverride),
            ("ssrf url validator accepts client error rejection", SsrfUrlValidationValidatorTests.SsrfUrlValidationAcceptsClientErrorRejection),
            ("ssrf url validator accepts safe rejection message", SsrfUrlValidationValidatorTests.SsrfUrlValidationAcceptsSafeRejectionMessage),
            ("ssrf url validator flags accepted url fields", SsrfUrlValidationValidatorTests.SsrfUrlValidationFlagsAcceptedUrlFields),
            ("ssrf url validator flags fetch related errors", SsrfUrlValidationValidatorTests.SsrfUrlValidationFlagsFetchRelatedErrors),
            ("ssrf url validator skips get endpoints", SsrfUrlValidationValidatorTests.SsrfUrlValidationSkipsGetEndpoints),
            ("cors validator accepts missing cors headers", CorsValidatorTests.CorsValidatorAcceptsMissingCorsHeaders),
            ("cors validator accepts strict allowlist", CorsValidatorTests.CorsValidatorAcceptsStrictAllowlist),
            ("cors validator flags wildcard origin", CorsValidatorTests.CorsValidatorFlagsWildcardOrigin),
            ("cors validator flags reflected credentialed origin", CorsValidatorTests.CorsValidatorFlagsReflectedCredentialedOrigin),
            ("cors validator flags unsafe preflight", CorsValidatorTests.CorsValidatorFlagsUnsafePreflight)
        };

        foreach (var test in tests)
        {
            await test.Run();
            Console.WriteLine("PASS " + test.Name);
        }
    }
}
