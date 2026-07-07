using BendIt.Api.Models;
using EndpointModel = BendIt.Api.Models.Endpoint;

namespace BendIt.Api.TestBattery.RobustnessTests;

internal sealed class RequestSizeTest : SingleRequestRobustnessTest
{
    public override string BendType => "requestSize";

    public override bool AppliesTo(EndpointModel endpoint, TestRunRequest request)
    {
        return RobustnessTestHelpers.AllowsRequestBody(endpoint.Method);
    }

    protected override string RequestBody(TestRunRequest request)
    {
        return RobustnessTestHelpers.JsonPayload("payload", RobustnessTestHelpers.LargestKb(request.BodySizesKb, 100));
    }

    protected override Dictionary<string, object> Mutation(TestRunRequest request)
    {
        return new Dictionary<string, object> { ["type"] = "bodyExpansion", ["sizeKb"] = RobustnessTestHelpers.LargestKb(request.BodySizesKb, 100) };
    }
}
