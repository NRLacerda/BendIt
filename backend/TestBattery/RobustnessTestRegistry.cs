using BendIt.Api.TestBattery.RobustnessTests;

namespace BendIt.Api.TestBattery;

internal sealed class RobustnessTestRegistry
{
    private readonly Dictionary<string, RobustnessTest> tests;

    public static RobustnessTestRegistry Default { get; } = new(
    [
        new DependencyResilienceTest(),
        new AuthConsistencyTest(),
        new JwtAnalysisTest(),
        new CookieAnalysisTest(),
        new HttpMethodValidationTest(),
        new PayloadValidationTest(),
        new RequestSizeTest(),
        new FieldSizeTest(),
        new MassAssignmentTest(),
        new SsrfUrlValidationTest(),
        new IdMutationTest(),
        new InventoryExposureTest(),
        new SecurityHeadersTest(),
        new SensitiveDataExposureTest(),
        new ResponseDiffingTest(),
        new ContentTypeValidationTest(),
        new ErrorDisclosureTest(),
        new ParameterPollutionTest(),
        new CorsAnalysisTest(),
        new RateLimitTest()
    ]);

    public RobustnessTestRegistry(IEnumerable<RobustnessTest> tests)
    {
        this.tests = tests.ToDictionary(test => test.BendType, StringComparer.OrdinalIgnoreCase);
    }

    public IEnumerable<RobustnessTest> Resolve(IEnumerable<string> bendTypes)
    {
        foreach (var bendType in bendTypes)
        {
            if (tests.TryGetValue(bendType, out var test))
            {
                yield return test;
            }
        }
    }
}
