# Test Battery Engine

Executes robustness test mutations against discovered endpoints to evaluate security status and input constraints.

## Inputs

- **Base URL** (`string`): Target project base host.
- **Endpoints** (`[]model.Endpoint`): List of verified endpoint candidates.
- **Test Request Settings** (`model.TestRunRequest`):
  - `bendTypes` (`[]string`): List of test types to run (e.g. `idMutation`, `massAssignment`, `requestSize`, `fieldSize`).
  - `parallelWorkers` (`int`): Worker pool capacity (defaults to 6).
  - `excludedPathPatterns` (`[]string`): Patterns of paths to skip.
- **Auth Config** (`model.AuthConfig`): Masked auth context details.

## Processing

1. **Endpoint Selection**:
   - Filters out endpoints that do not match testable categories (checks if classification is `confirmed`, `likely_exists`, `protected`, or `method_not_allowed` and has confidence >= 60).
   - `likely_exists` endpoints are included so healthy non-JSON endpoints (for example `200 true` liveness checks) still produce raw evidence instead of an empty `results.json`.
   - Skips endpoints matching path exclusion patterns.
2. **Job Dispatching**:
   - Loops through every testable endpoint and selected test type (`bendType`), creating individual jobs.
   - Computes the planned job count before execution so run progress can advance during the test phase.
3. **Audit Execution**:
   - Processes each job sequentially in the current implementation:
     - **Mutation**: Mutates request properties depending on the `bendType`:
       - `idMutation` / `authConsistency`: Alters identification elements in URL variables (e.g. changing resource ID from `123` to `124`).
       - `massAssignment`: Injects administrative/extra parameters into JSON request bodies.
       - `fieldSize` / `requestSize`: Expands body/field payloads with massive string lengths.
     - **Execution**: Sends the HTTP request with the target mutation payload.
     - **Response Evaluation**: Parses returned status code and response size.
   - Reports completed job count and finding count back to the run coordinator after each result so `current-run.json` can be polled by the frontend execution page.
4. **Risk & Verdict Assignment**:
   - Calculates a risk rating (0–10) based on response status variations.
   - Public endpoints without authentication context, such as liveness checks returning `200 true`, remain raw healthy evidence unless the selected mutation has endpoint context that supports a finding.
   - Categorizes outcomes:
     - **Finding / Vulnerable** (`risk >= 5`): If mutated authorization checks return `200 OK` (e.g., accessed someone else's resource), or if size limit tests crash.
     - **Secure / Blocked** (`risk < 5`): If mutated requests return expected `4xx` errors (e.g. `400 Bad Request`, `403 Forbidden`, `413 Payload Too Large`).
5. **Output Writing**:
   - Packages outcomes into `TestResult` structs and saves them.

## Outputs

- **Test Results** (`[]model.TestResult`): List of results detailing targeted URL, HTTP method, applied mutation payload, response details, risk rating, severity label, and finding status.
