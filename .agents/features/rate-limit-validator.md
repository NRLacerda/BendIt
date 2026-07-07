# Rate Limit Validator

Checks whether a target endpoint has observable throttling controls without running an unsafe load test.

## Inputs

- **Endpoint** (`Endpoint`): Method, host, port, path, and query metadata from discovery.
- **Project Headers/Auth** (`Project`): Request headers and masked auth context used by other test battery requests.
- **Test Settings** (`TestRunRequest`):
  - `bendTypes`: Includes `rateLimit`.
  - `maxRequestsPerEndpoint`: Optional burst request count. Defaults to `5`, has a minimum of `2`, and is hard-capped at `10`.

## Processing

1. Sends one baseline request and captures status, headers, content type, bounded body, and body size.
2. Sends the burst with bounded parallelism:
   - Burst request count is capped at `10`.
   - Burst concurrency defaults to `3` and is hard-capped at `3`.
   - Requests use the same endpoint URL, project headers, auth behavior, timeout, and bounded response capture as normal test battery requests.
3. Sends one post-burst comparison request after every burst request completes.
4. Detects throttling and degradation:
   - Looks for HTTP `429` across baseline, burst, and post-burst responses.
   - Looks for throttling headers: `Retry-After`, `RateLimit`, `RateLimit-Limit`, `RateLimit-Remaining`, `RateLimit-Reset`, and `X-RateLimit-*`.
   - Compares baseline and post-burst status, content type, body hash, body size class, and throttling-header state.

## Outputs

- Emits one `TestResult` with `bendType = rateLimit`.
- Stores the post-burst response in the normal `result` and `resultBody` fields.
- Stores structured burst evidence in `mutation`, including:
  - `burstRequests`
  - `burstConcurrency`
  - `burstStatuses`
  - `saw429`
  - `sawThrottlingHeaders`
  - `throttlingHeaders`
  - `degradedAfterBurst`
  - `degradationSignals`
- Evidence summarizes the baseline/burst/post-burst sequence, statuses, missing throttling signals, throttling headers, and any degraded post-burst behavior.

## Risk Behavior

- Low risk when throttling controls are observed and the post-burst response matches the baseline.
- Medium risk when no HTTP `429` is observed, no throttling headers are observed, any request fails, or the post-burst response differs from the baseline.
- Maps to `API4: Unrestricted Resource Consumption`.
