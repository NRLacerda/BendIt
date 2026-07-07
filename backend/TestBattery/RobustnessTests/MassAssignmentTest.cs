using BendIt.Api.Models;
using EndpointModel = BendIt.Api.Models.Endpoint;

namespace BendIt.Api.TestBattery.RobustnessTests;

internal sealed class MassAssignmentTest : SingleRequestRobustnessTest
{
    public override string BendType => "massAssignment";

    public override bool AppliesTo(EndpointModel endpoint, TestRunRequest request)
    {
        return RobustnessTestHelpers.AllowsRequestBody(endpoint.Method);
    }

    protected override string RequestBody(TestRunRequest request)
    {
        return """{"role":"ADMIN","isAdmin":true,"tenantId":"other-tenant"}""";
    }

    protected override Dictionary<string, object> Mutation(TestRunRequest request)
    {
        return new Dictionary<string, object> { ["type"] = "extraFields", ["fields"] = new[] { "role", "isAdmin", "tenantId" } };
    }
}
