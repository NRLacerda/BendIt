using BendIt.Api.Models;
using EndpointModel = BendIt.Api.Models.Endpoint;

namespace BendIt.Api.TestBattery.RobustnessTests;

internal sealed class FieldSizeTest : SingleRequestRobustnessTest
{
    public override string BendType => "fieldSize";

    public override bool AppliesTo(EndpointModel endpoint, TestRunRequest request)
    {
        return RobustnessTestHelpers.AllowsRequestBody(endpoint.Method);
    }

    protected override string RequestBody(TestRunRequest request)
    {
        return RobustnessTestHelpers.JsonPayload("description", RobustnessTestHelpers.LargestKb(request.FieldSizesKb, 50));
    }

    protected override Dictionary<string, object> Mutation(TestRunRequest request)
    {
        return new Dictionary<string, object> { ["type"] = "fieldExpansion", ["sizeKb"] = RobustnessTestHelpers.LargestKb(request.FieldSizesKb, 50) };
    }
}
