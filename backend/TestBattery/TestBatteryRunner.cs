using BendIt.Api.Models;
using BendIt.Api.TestBattery.RobustnessTests;
using EndpointModel = BendIt.Api.Models.Endpoint;

namespace BendIt.Api.TestBattery;

public sealed class TestBatteryRunner
{
    private static readonly string[] DefaultBendTypes =
    [
        "authConsistency",
        "jwtAnalysis",
        "httpMethodValidation",
        "payloadValidation",
        "requestSize",
        "fieldSize",
        "massAssignment",
        "idMutation",
        "inventoryExposure",
        "securityHeaders",
        "sensitiveDataExposure",
        "responseDiffing"
    ];

    private readonly RobustnessTestRegistry tests;

    public TestBatteryRunner()
        : this(RobustnessTestRegistry.Default)
    {
    }

    internal TestBatteryRunner(RobustnessTestRegistry tests)
    {
        this.tests = tests;
    }

    public Task<ResultsDocument> RunAsync(Project project, IReadOnlyList<EndpointModel> endpoints, TestRunRequest request, CancellationToken cancellationToken)
    {
        return RunAsync(project, endpoints, request, null, cancellationToken);
    }

    public async Task<ResultsDocument> RunAsync(
        Project project,
        IReadOnlyList<EndpointModel> endpoints,
        TestRunRequest request,
        Func<int, int, int, Task>? progressCallback,
        CancellationToken cancellationToken)
    {
        IEnumerable<string> selectedBendTypes = request.BendTypes.Count == 0 ? DefaultBendTypes : request.BendTypes;
        var selectedTests = tests.Resolve(selectedBendTypes).ToList();
        var plannedJobs = endpoints
            .Where(endpoint => !RobustnessTestHelpers.Excluded(endpoint.Path, request.ExcludedPathPatterns))
            .SelectMany(endpoint => selectedTests.Where(test => test.AppliesTo(endpoint, request)))
            .Count();
        var results = new List<TestResult>();
        var testRunId = "run_" + RobustnessTestHelpers.ShortHash(project.ProjectId + DateTimeOffset.UtcNow.ToString("O"));

        using var client = new HttpClient { Timeout = RobustnessTestHelpers.RequestTimeout };
        foreach (var endpoint in endpoints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (RobustnessTestHelpers.Excluded(endpoint.Path, request.ExcludedPathPatterns))
            {
                continue;
            }

            foreach (var test in selectedTests)
            {
                if (!test.AppliesTo(endpoint, request))
                {
                    continue;
                }

                var context = new RobustnessTestContext(client, project, endpoint, request, testRunId);
                var result = await test.RunAsync(context, cancellationToken);
                results.Add(result);

                if (progressCallback is not null)
                {
                    await progressCallback(results.Count, plannedJobs, results.Count(item => item.Interesting));
                }
            }
        }

        return new ResultsDocument
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            ProjectId = project.ProjectId,
            Results = results
        };
    }
}
