using BendIt.Api.Models;
using EndpointModel = BendIt.Api.Models.Endpoint;

namespace BendIt.Api.TestBattery;

public interface RobustnessTest
{
    string BendType { get; }
    bool AppliesTo(EndpointModel endpoint, TestRunRequest request);
    Task<TestResult> RunAsync(RobustnessTestContext context, CancellationToken cancellationToken);
}

public sealed record RobustnessTestContext(
    HttpClient Client,
    Project Project,
    EndpointModel Endpoint,
    TestRunRequest Request,
    string TestRunId);
