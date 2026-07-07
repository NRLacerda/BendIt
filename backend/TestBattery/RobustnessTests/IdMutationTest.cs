using BendIt.Api.Models;
using EndpointModel = BendIt.Api.Models.Endpoint;

namespace BendIt.Api.TestBattery.RobustnessTests;

internal sealed class IdMutationTest : SingleRequestRobustnessTest
{
    public override string BendType => "idMutation";

    public override bool AppliesTo(EndpointModel endpoint, TestRunRequest request)
    {
        return RobustnessTestHelpers.HasMutableIdentifier(endpoint);
    }

    protected override Dictionary<string, object> Mutation(TestRunRequest request)
    {
        return new Dictionary<string, object>
        {
            ["type"] = "pathIdMutation",
            ["field"] = "id",
            ["originalValue"] = "123",
            ["mutatedValue"] = "124"
        };
    }
}
