using System.Text.RegularExpressions;
using BendIt.Api.Models;
using BendIt.Api.Runner;
using BendIt.Api.Storage;
using Microsoft.AspNetCore.Mvc;

namespace BendIt.Api.Controllers;

[ApiController]
[Route("api")]
public sealed partial class RunnerController(IProjectStore store, RunCoordinator runCoordinator) : ControllerBase
{
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "ok", startedAt = _startedAt, resultsDir = store.Root });
    }

    [HttpGet("projects")]
    public async Task<IActionResult> Projects(CancellationToken cancellationToken)
    {
        var projects = await store.ListProjectsAsync(cancellationToken);
        return Ok(new { projects });
    }

    [HttpPost("projects")]
    public async Task<IActionResult> CreateProject([FromBody] Project project, CancellationToken cancellationToken)
    {
        var normalized = NormalizeProject(project, store.Root);
        await store.SaveProjectAsync(normalized, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, normalized);
    }

    [HttpPut("projects/{projectId}")]
    public async Task<IActionResult> UpdateProject(string projectId, [FromBody] Project project, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await store.LoadProjectAsync(projectId, cancellationToken);
            var normalized = NormalizeProject(project, store.Root);
            normalized.ProjectId = existing.ProjectId;
            normalized.OutputDir = Path.Combine(store.Root, existing.ProjectId).Replace(Path.DirectorySeparatorChar, '/');
            normalized.CreatedAt = existing.CreatedAt;
            normalized.UpdatedAt = DateTimeOffset.UtcNow;
            await store.SaveProjectAsync(normalized, cancellationToken);
            return Ok(normalized);
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpGet("projects/{projectId}")]
    public async Task<IActionResult> Project(string projectId, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await store.LoadProjectAsync(projectId, cancellationToken));
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpGet("projects/{projectId}/endpoints")]
    public async Task<IActionResult> Endpoints(string projectId, CancellationToken cancellationToken)
    {
        return Ok(await store.LoadEndpointsAsync(projectId, cancellationToken));
    }

    [HttpPost("projects/{projectId}/run")]
    public async Task<IActionResult> StartRun(string projectId, [FromBody] RunRequest? request, CancellationToken cancellationToken)
    {
        try
        {
            var run = await runCoordinator.StartRunAsync(projectId, request, cancellationToken);
            return Accepted(run);
        }
        catch (RunConflictException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpGet("projects/{projectId}/runs/current")]
    public async Task<IActionResult> CurrentRun(string projectId, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await store.LoadCurrentRunAsync(projectId, cancellationToken));
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { error = "no run has been started for this project" });
        }
    }

    [HttpGet("projects/{projectId}/runs")]
    public async Task<IActionResult> Runs(string projectId, CancellationToken cancellationToken)
    {
        try
        {
            var runs = await store.ListRunsAsync(projectId, cancellationToken);
            return Ok(new { runs });
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpGet("projects/{projectId}/runs/{runId}")]
    public async Task<IActionResult> Run(string projectId, string runId, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await store.LoadRunAsync(projectId, runId, cancellationToken));
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { error = "run not found" });
        }
    }

    [HttpGet("projects/{projectId}/runs/{runId}/results")]
    public async Task<IActionResult> RunResults(string projectId, string runId, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await store.LoadRunResultsAsync(projectId, runId, cancellationToken));
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { error = "run results not found" });
        }
    }

    [HttpGet("projects/{projectId}/results")]
    public async Task<IActionResult> Results(string projectId, CancellationToken cancellationToken)
    {
        return Ok(await store.LoadResultsAsync(projectId, cancellationToken));
    }

    private static Project NormalizeProject(Project project, string resultsRoot)
    {
        if (!Uri.TryCreate(project.BaseUrl, UriKind.Absolute, out var uri) ||
            string.IsNullOrEmpty(uri.Scheme) ||
            string.IsNullOrEmpty(uri.Host))
        {
            throw new BadHttpRequestException("baseUrl must be an absolute URL");
        }

        if (uri.Scheme is not "http" and not "https")
        {
            throw new BadHttpRequestException("baseUrl must use http or https");
        }

        project.ProjectId = Slug(project.ProjectId);
        if (string.IsNullOrEmpty(project.ProjectId))
        {
            project.ProjectId = !string.IsNullOrEmpty(project.Name) ? Slug(project.Name) : Slug(uri.Host);
        }

        if (string.IsNullOrEmpty(project.Name))
        {
            project.Name = project.ProjectId;
        }

        project.BaseUrl = project.IsWebPage
            ? uri.GetLeftPart(UriPartial.Path).TrimEnd('/')
            : uri.Scheme + "://" + uri.Authority;
        project.OutputDir = Path.Combine(resultsRoot, project.ProjectId).Replace(Path.DirectorySeparatorChar, '/');
        var now = DateTimeOffset.UtcNow;
        project.CreatedAt = project.CreatedAt == default ? now : project.CreatedAt;

        project.UpdatedAt = now;
        if (string.IsNullOrEmpty(project.Auth.Type))
        {
            project.Auth.Type = "none";
        }

        return project;
    }

    private static string Slug(string value)
    {
        value = ProjectIdChars().Replace(value.Trim().ToLowerInvariant(), "-").Trim('-');
        return value.Length > 80 ? value[..80].Trim('-') : value;
    }

    [GeneratedRegex("[^a-z0-9-]+")]
    private static partial Regex ProjectIdChars();
}
