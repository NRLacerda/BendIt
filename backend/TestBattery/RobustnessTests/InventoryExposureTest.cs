using BendIt.Api.Models;
using EndpointModel = BendIt.Api.Models.Endpoint;

namespace BendIt.Api.TestBattery.RobustnessTests;

internal sealed class InventoryExposureTest : SingleRequestRobustnessTest
{
    public override string BendType => "inventoryExposure";

    public override bool AppliesTo(EndpointModel endpoint, TestRunRequest request)
    {
        return RobustnessTestHelpers.InventorySignals(endpoint).Count > 0;
    }
}
