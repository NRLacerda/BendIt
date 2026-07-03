using System.Security.Cryptography;
using System.Text;
using BendIt.Api.Models;
using BendIt.Api.Storage;

namespace BendIt.Api.Runner;

public sealed class RunCoordinator(
    IProjectStore store,
    ActiveRunRegistry activeRunRegistry,
    RunExecutionService executionService)
{
    public async Task<RunDocument> StartRunAsync(string projectId, RunRequest? request, CancellationToken cancellationToken)
    {
        var project = await store.LoadProjectAsync(projectId, cancellationToken);
        request ??= new RunRequest
        {
            Discovery = new DiscoveryRequest
            {
                UseNativeApiList = true,
                UseSpecDiscovery = true
            }
        };

        if (!activeRunRegistry.Reserve(projectId))
        {
            throw new RunConflictException("a run is already active for this project");
        }

        var run = new RunDocument
        {
            RunId = NewRunId(projectId),
            ProjectId = projectId,
            Status = "queued",
            CurrentStep = "api_specs",
            Progress = 0,
            StartedAt = DateTimeOffset.UtcNow
        };

        try
        {
            await store.SaveRunAsync(projectId, run, cancellationToken);
        }
        catch
        {
            activeRunRegistry.Release(projectId);
            throw;
        }

        _ = Task.Run(() => executionService.ExecuteRunAsync(project, request, run, CancellationToken.None));
        return run;
    }

    private static string NewRunId(string projectId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(projectId + DateTimeOffset.UtcNow.ToString("O")));
        return "run-" + Convert.ToHexString(bytes).ToLowerInvariant()[..12];
    }
}

public sealed class RunConflictException(string message) : Exception(message);
