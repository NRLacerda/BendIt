# Project JSON Storage

Manages project configurations, current run steps, discovered endpoints, and test results as structured local JSON documents.

## Inputs

- **Root Directory** (`resultsRoot`): The root directory where files are stored (defaults to `bend-results/`).
- **Project Context** (`Project`): Configuration credentials, description, base URL, target type flag (`isWebPage`), and auth context. WebAPI project URLs normalize to the origin; WebPage project URLs preserve the configured page path.
- **Run Progress** (`model.RunDocument`): Active state, step label, percentage progress.
- **Endpoint Discovery Data** (`model.EndpointsDocument`): List of registered endpoints.
- **Audit Outcomes** (`model.ResultsDocument`): List of test result artifacts.

## Processing

1. **Project Directory Scoping**:
   - Generates unique folders for projects based on the sanitized `projectId` slug (e.g. `bend-results/my-project/`).
2. **Metadata Masking**:
   - Strips and masks sensitive plain-text auth parameters (raw JWTs, cookies, and custom headers keys) before saving them to disk (replaces them with masked templates like `***`).
3. **JSON Serialization**:
   - Saves objects into distinct files:
     - `project.json`: Config metadata.
     - `endpoints.json`: Discovered endpoints lists.
     - `results.json`: Execution results and findings.
     - `current-run.json`: Stores live pipeline progress statistics.
     - `runs/run-xxxxxxxxxxxx.json`: Historical execution statistics.
4. **Path Template Normalization**:
   - Collapses paths to unified templates (replaces numeric and hex ID blocks with `{id}`) during write operations to avoid duplication.

## Outputs

- **Stored Artifacts**: Standardized JSON files on the local disk.
