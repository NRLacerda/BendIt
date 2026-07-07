# Rate Limit Validator

Runs a bounded rate-limit audit against a selected endpoint by sending real requests in a controlled sequence and evaluating throttling behavior.

## Inputs

- **Discovered Endpoint** (`Endpoint`): A testable endpoint selected by the test battery.
- **Project Config** (`Project`): Base request headers and masked authentication context.
- **Test Request Settings** (`TestRunRequest`):
  - `bendTypes`: Must include `rateLimit`.
  - `maxRequestsPerEndpoint`: Optional burst size. Defaults to 5, clamps to a minimum of 2 and a maximum of 10.

## Processing

1. **Request Sequence**:
   - Sends one baseline request to the concrete endpoint URL.
   - Sends the sequential burst using the clamped burst size.
   - Sends one post-burst comparison request.
   - Uses the same bounded response capture, timeout, project headers, and auth handling as normal test execution.
2. **Throttle Detection**:
   - Records whether any baseline, burst, or post-burst response returned HTTP `429`.
   - Records common throttling headers: `Retry-After`, `RateLimit`, `RateLimit-Limit`, `RateLimit-Remaining`, `RateLimit-Reset`, and `X-RateLimit-*`.
3. **Degradation Detection**:
   - Compares baseline and post-burst response status, content type, body size class, body hash, and throttling-header state.
   - Marks degraded behavior when any comparison signal changes.
4. **Risk Evaluation**:
   - Low risk when HTTP `429`, throttling headers, and stable post-burst behavior are observed.
   - Medium risk when no `429` is observed, throttling headers are missing, any request fails, or post-burst behavior degrades.

## Outputs

- **Test Result** (`TestResult`): Uses the existing result schema with the post-burst response in `result` and `resultBody`.
- **Mutation Metadata**: Stores `type=controlledRateLimitBurst`, baseline status, burst count, burst statuses, post-burst status, `saw429`, `sawThrottlingHeaders`, throttling header names, `degradedAfterBurst`, and degradation signals.
- **Evidence**: Summarizes request count, observed statuses, missing `429`, missing headers, and any post-burst degradation.

## Safety

- Burst requests are sequential, not parallel.
- Burst size is capped at 10 even when the request asks for a larger value.
- The validator remains opt-in through `bendTypes` and does not run as part of the default bend type list.
