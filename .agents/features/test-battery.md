# Test Battery Engine

Executes robustness test mutations against discovered endpoints to evaluate security status and input constraints.

## Inputs

- **Base URL** (`string`): Target project base host.
- **Endpoints** (`[]model.Endpoint`): List of verified endpoint candidates.
- **Test Request Settings** (`model.TestRunRequest`):
  - `bendTypes` (`[]string`): List of test types to run (e.g. `idMutation`, `massAssignment`, `requestSize`, `fieldSize`, `rateLimit`, `securityHeaders`, `inventoryExposure`, `sensitiveDataExposure`).
  - `maxRequestsPerEndpoint` (`int`): Optional per-endpoint burst count used by `rateLimit`; values are clamped by the validator safety limit.
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
       - `idMutation`: Alters identification elements in path variables, numeric/UUID path segments, or identifier-like query parameters.
       - `authConsistency` / `jwtAnalysis` / `cookieAnalysis`: Checks missing or malformed authentication boundaries without persisting raw secrets.
       - `massAssignment`: Injects administrative/extra parameters into JSON request bodies.
       - `fieldSize` / `requestSize`: Expands body/field payloads with massive string lengths.
       - `securityHeaders`: Reviews captured response headers for common hardening gaps.
       - `inventoryExposure`: Flags live versioned, legacy, deprecated, internal, debug, documentation, and operational routes.
       - `sensitiveDataExposure`: Reviews bounded response bodies for sensitive data classes and records only field/data categories, not values.
       - `rateLimit`: Sends a baseline request, a sequential controlled burst, and a post-burst comparison request. The burst uses `maxRequestsPerEndpoint` when provided, defaults to 5, and is clamped between 2 and 10 burst requests.
     - **Execution**: Sends the HTTP request with the target mutation payload.
     - **Response Evaluation**: Parses returned status code, response size, masked headers, bounded body, OWASP category, and remediation recommendation.
       - `rateLimit` evaluates whether the burst produced HTTP 429, whether common throttling headers were present, and whether the post-burst response changed status, content type, body size/hash, or throttling-header state compared with the baseline.
   - Reports completed job count and finding count back to the run coordinator after each result so `current-run.json` can be polled by the frontend execution page.
4. **Risk & Verdict Assignment**:
   - Calculates a risk rating (0-10) based on response status, endpoint context, response headers, inventory signals, sensitive data indicators, and rate-limit burst observations.
   - Public endpoints without authentication context, such as liveness checks returning `200 true`, remain raw healthy evidence unless the selected mutation has endpoint context that supports a finding.
   - Categorizes outcomes:
     - **Finding / Vulnerable** (`risk >= 5`): If mutated authorization checks return `200 OK` (e.g., accessed someone else's resource), or if size limit tests crash.
     - **Suspicious Rate Limit Behavior** (`risk >= 5`): If a controlled rate-limit burst observes no HTTP 429, no throttling headers, request failures, or degraded post-burst behavior.
     - **Secure / Blocked** (`risk < 5`): If mutated requests return expected `4xx` errors (e.g. `400 Bad Request`, `403 Forbidden`, `413 Payload Too Large`).
     - **Rate-limited / Healthy** (`risk < 5`): If `rateLimit` observes throttling controls such as HTTP 429 or throttling headers and the post-burst response still matches the baseline.
5. **Output Writing**:
   - Packages outcomes into `TestResult` structs and saves them.

## Outputs

- **Test Results** (`[]model.TestResult`): List of results detailing targeted URL, HTTP method, applied mutation payload, masked request/response headers, response details, OWASP category, remediation recommendation, risk rating, severity label, and finding status.
