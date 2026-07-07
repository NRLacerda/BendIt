using BendIt.Api.Models;

namespace BendIt.Api.Storage;

public interface IProjectStore
{
    string Root { get; }
    Task SaveProjectAsync(Project project, CancellationToken cancellationToken);
    Task<Project> LoadProjectAsync(string projectId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Project>> ListProjectsAsync(CancellationToken cancellationToken);
    Task SaveEndpointsAsync(string projectId, EndpointsDocument document, CancellationToken cancellationToken);
    Task<EndpointsDocument> LoadEndpointsAsync(string projectId, CancellationToken cancellationToken);
    Task SaveResultsAsync(string projectId, ResultsDocument document, CancellationToken cancellationToken);
    Task SaveRunResultsAsync(string projectId, string runId, ResultsDocument document, CancellationToken cancellationToken);
    Task<ResultsDocument> LoadResultsAsync(string projectId, CancellationToken cancellationToken);
    Task<ResultsDocument> LoadRunResultsAsync(string projectId, string runId, CancellationToken cancellationToken);
    Task SaveRunAsync(string projectId, RunDocument document, CancellationToken cancellationToken);
    Task<RunDocument> LoadCurrentRunAsync(string projectId, CancellationToken cancellationToken);
    Task<RunDocument> LoadRunAsync(string projectId, string runId, CancellationToken cancellationToken);
    Task<IReadOnlyList<RunDocument>> ListRunsAsync(string projectId, CancellationToken cancellationToken);
}
