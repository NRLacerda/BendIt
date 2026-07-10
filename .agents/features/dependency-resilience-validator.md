# Dependency Resilience Validator

## Purpose

The dependency resilience validator detects whether repeated safe access causes backend dependency or connection-pool failures. It is designed to catch symptoms such as Mongo socket timeouts, exhausted database pools, and downstream connection failures early in a run.

## Inputs

- **Discovered Endpoint** (`Endpoint`): Any endpoint selected by the test battery.
- **Project Config** (`Project`): Base request headers and masked authentication context.
- **Test Request Settings** (`TestRunRequest`):
  - `bendTypes`: Includes `dependencyResilience` by default.
  - `downDetectionThreshold`: Defaults to `5`, clamps to `3..10`, and disables this validator when explicitly set to `0`.

## Processing

1. **Repeated Request Probe**:
   - Sends the same concrete endpoint request sequentially `downDetectionThreshold` times.
   - Uses the normal BendIt request timeout, auth/header handling, and bounded body capture.
2. **Dependency Signal Detection**:
   - Searches responses and request errors for dependency failure indicators such as `MongoTimeoutException`, `MongoSocketOpenException`, `Timed out while waiting for a server`, `Connect timed out`, `HikariPool`, JDBC errors, Redis connection errors, `ECONNREFUSED`, `ETIMEDOUT`, connection-pool errors, and unavailable connection messages.
3. **Stability and Streak Detection**:
   - Builds normalized response fingerprints from status, content type, and response body.
   - Records the longest repeated failure streak so the runner can stop early if the API appears down or dependency-exhausted.

## Outputs

- **Test Result** (`TestResult`): One aggregated result for the endpoint using the most relevant observed response.
- **Mutation Metadata**: Stores `type=controlledDependencyProbe`, request count, statuses, stable fingerprint flag, failure streak, and dependency signals.
- **Evidence**: States whether repeated access stayed stable, degraded, or showed potential backend dependency resource exhaustion.
- **OWASP Mapping**: Maps to `API4: Unrestricted Resource Consumption`.

## Safety

- Requests are sequential, not parallel.
- The threshold is capped at 10 requests.
- Findings are worded as potential backend dependency resource exhaustion, not confirmed connection leaks or missing pooling.
