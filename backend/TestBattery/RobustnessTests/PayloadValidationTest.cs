using BendIt.Api.Models;
using EndpointModel = BendIt.Api.Models.Endpoint;

namespace BendIt.Api.TestBattery.RobustnessTests;

internal sealed class PayloadValidationTest : SingleRequestRobustnessTest
{
    public override string BendType => "payloadValidation";

    public override bool AppliesTo(EndpointModel endpoint, TestRunRequest request)
    {
        return RobustnessTestHelpers.AllowsRequestBody(endpoint.Method);
    }

    protected override string RequestBody(TestRunRequest request)
    {
        return """{"payload":""";
    }
}
