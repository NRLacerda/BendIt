using BendIt.Api.Discovery;
using BendIt.Api.Models;
using BendIt.Api.Storage;
using BendIt.Api.TestBattery;

namespace BendIt.Api.Runner;

public sealed class RunExecutionService(
    IProjectStore store,
    ActiveRunRegistry activeRunRegistry,
    DiscoveryOrchestrator discoveryOrchestrator,
    TestBatteryRunner testBatteryRunner)
{
    public async Task ExecuteRunAsync(Project project, RunRequest request, RunDocument run, CancellationToken cancellationToken)
    {
        var projectId = project.ProjectId;
        try
        {
            async Task UpdateAsync(string step, string status, int progress)
            {
                run.CurrentStep = step;
                run.Status = status;
                run.Progress = progress;
                await store.SaveRunAsync(projectId, run, cancellationToken);
            }

            await UpdateAsync("api_specs", "running", 10);
            var existing = await store.LoadEndpointsAsync(projectId, cancellationToken);

            await UpdateAsync("discovery", "running", 25);
            var endpoints = await discoveryOrchestrator.RunAsync(project, request.Discovery, existing.Endpoints, cancellationToken);
            run.EndpointCount = endpoints.Endpoints.Count;
            await store.SaveEndpointsAsync(projectId, endpoints, cancellationToken);

            await UpdateAsync("tests", "running", 55);
            var testableEndpoints = DiscoveryOrchestrator.FilterTestableEndpoints(endpoints.Endpoints);
            var results = await testBatteryRunner.RunAsync(
                project,
                testableEndpoints,
                request.Tests,
                async (completed, total, findings) =>
                {
                    run.ResultCount = completed;
                    run.FindingCount = findings;
                    run.Progress = total <= 0 ? 80 : 55 + (int)Math.Round(Math.Min(1, completed / (double)total) * 25);
                    await store.SaveRunAsync(projectId, run, cancellationToken);
                },
                cancellationToken);
            run.ResultCount = results.Results.Count;
            run.FindingCount = results.Results.Count(result => result.Interesting);
            await store.SaveResultsAsync(projectId, results, cancellationToken);

            await UpdateAsync("results", "running", 82);
            await UpdateAsync("analysis", "running", 95);
            run.Status = "completed";
            run.Progress = 100;
            run.CompletedAt = DateTimeOffset.UtcNow;
            await store.SaveRunAsync(projectId, run, cancellationToken);
        }
        catch (Exception ex)
        {
            run.Status = "failed";
            run.Error = ex.Message;
            run.CompletedAt = DateTimeOffset.UtcNow;
            if (run.Progress < 1)
            {
                run.Progress = 1;
            }

            await store.SaveRunAsync(projectId, run, CancellationToken.None);
        }
        finally
        {
            activeRunRegistry.Release(projectId);
        }
    }
}
