using System.Text.Json;
using System.Text.RegularExpressions;
using BendIt.Api.Models;

namespace BendIt.Api.Storage;

public sealed partial class JsonProjectStore : IProjectStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _runLock = new(1, 1);

    public JsonProjectStore(string root)
    {
        Root = string.IsNullOrWhiteSpace(root) ? "bend-results" : root;
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public async Task SaveProjectAsync(Project project, CancellationToken cancellationToken)
    {
        var dir = ProjectDir(project.ProjectId);
        Directory.CreateDirectory(Path.Combine(dir, "reports"));
        await WriteJsonAsync(Path.Combine(dir, "project.json"), project, cancellationToken);
    }

    public async Task<Project> LoadProjectAsync(string projectId, CancellationToken cancellationToken)
    {
        return await ReadJsonAsync<Project>(ProjectFile(projectId, "project.json"), cancellationToken);
    }

    public async Task<IReadOnlyList<Project>> ListProjectsAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(Root))
        {
            return [];
        }

        var projects = new List<Project>();
        foreach (var dir in Directory.EnumerateDirectories(Root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                projects.Add(await LoadProjectAsync(Path.GetFileName(dir), cancellationToken));
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Ignore malformed non-project directories, matching the Go store behavior.
            }
        }

        return projects;
    }

    public async Task SaveEndpointsAsync(string projectId, EndpointsDocument document, CancellationToken cancellationToken)
    {
        await WriteJsonAsync(ProjectFile(projectId, "endpoints.json"), document, cancellationToken);
    }

    public async Task<EndpointsDocument> LoadEndpointsAsync(string projectId, CancellationToken cancellationToken)
    {
        var path = ProjectFile(projectId, "endpoints.json");
        return File.Exists(path) ? await ReadJsonAsync<EndpointsDocument>(path, cancellationToken) : new EndpointsDocument();
    }

    public async Task SaveResultsAsync(string projectId, ResultsDocument document, CancellationToken cancellationToken)
    {
        await WriteJsonAsync(ProjectFile(projectId, "results.json"), document, cancellationToken);
    }

    public async Task<ResultsDocument> LoadResultsAsync(string projectId, CancellationToken cancellationToken)
    {
        var path = ProjectFile(projectId, "results.json");
        return File.Exists(path) ? await ReadJsonAsync<ResultsDocument>(path, cancellationToken) : new ResultsDocument();
    }

    public async Task SaveRunAsync(string projectId, RunDocument document, CancellationToken cancellationToken)
    {
        await _runLock.WaitAsync(cancellationToken);
        try
        {
            var dir = ProjectDir(projectId);
            await WriteJsonAsync(Path.Combine(dir, "current-run.json"), document, cancellationToken);
            if (string.IsNullOrWhiteSpace(document.RunId))
            {
                return;
            }

            if (!SafeId().IsMatch(document.RunId))
            {
                throw new InvalidOperationException($"invalid runId {document.RunId}");
            }

            await WriteJsonAsync(Path.Combine(dir, "runs", document.RunId + ".json"), document, cancellationToken);
        }
        finally
        {
            _runLock.Release();
        }
    }

    public async Task<RunDocument> LoadCurrentRunAsync(string projectId, CancellationToken cancellationToken)
    {
        await _runLock.WaitAsync(cancellationToken);
        try
        {
            return await ReadJsonAsync<RunDocument>(ProjectFile(projectId, "current-run.json"), cancellationToken);
        }
        finally
        {
            _runLock.Release();
        }
    }

    public async Task<RunDocument> LoadRunAsync(string projectId, string runId, CancellationToken cancellationToken)
    {
        if (!SafeId().IsMatch(runId))
        {
            throw new InvalidOperationException($"invalid runId {runId}");
        }

        await _runLock.WaitAsync(cancellationToken);
        try
        {
            return await ReadJsonAsync<RunDocument>(Path.Combine(ProjectDir(projectId), "runs", runId + ".json"), cancellationToken);
        }
        finally
        {
            _runLock.Release();
        }
    }

    private string ProjectFile(string projectId, string name) => Path.Combine(ProjectDir(projectId), name);

    private string ProjectDir(string projectId)
    {
        if (!SafeId().IsMatch(projectId))
        {
            throw new InvalidOperationException($"invalid projectId {projectId}");
        }

        var rootAbs = Path.GetFullPath(Root);
        var dirAbs = Path.GetFullPath(Path.Combine(Root, projectId));
        if (!dirAbs.Equals(rootAbs, StringComparison.OrdinalIgnoreCase) &&
            !dirAbs.StartsWith(rootAbs + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("project path escapes results directory");
        }

        return dirAbs;
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = Path.Combine(Path.GetDirectoryName(path)!, ".tmp-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await using (var stream = File.Create(tmp))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
            }

            File.Move(tmp, path, true);
        }
        finally
        {
            if (File.Exists(tmp))
            {
                File.Delete(tmp);
            }
        }
    }

    private static async Task<T> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException($"unable to read {path}");
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,80}$")]
    private static partial Regex SafeId();
}
