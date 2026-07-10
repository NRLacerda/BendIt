# API Down Guard

## Purpose

The API down guard stops the test battery when the target repeatedly returns the same failure response. This avoids spending additional requests after the API is likely down, dependency-exhausted, or returning a persistent backend failure.

## Inputs

- **Test Results** (`TestResult`): Results emitted by the test battery runner.
- **Test Request Settings** (`TestRunRequest.downDetectionThreshold`): Defaults to `5`, clamps to `3..10`, and disables the guard when explicitly set to `0`.

## Processing

1. **Failure Classification**:
   - Counts only failure-like results: request status `0`, HTTP `5xx`, or responses containing known dependency/pool/socket failure signals.
2. **Fingerprinting**:
   - Builds a normalized fingerprint from status code, content type class, and response text.
   - Normalization removes timestamps, UUIDs, numbers, durations, line numbers, and repeated whitespace so near-identical error responses match.
3. **Run Stop**:
   - When `downDetectionThreshold` sequential failure-like results share the same fingerprint, the runner emits an `apiDownGuard` finding and stops the whole test battery.
   - If `dependencyResilience` reports an equal or larger repeated failure streak inside its own repeated probe, the guard can stop immediately after that validator result.

## Outputs

- **Synthetic Test Result** (`apiDownGuard`): Records the threshold, streak, trigger result, trigger test type, fingerprint, and `stoppedRemainingTests=true`.
- **Evidence**: States that the run was stopped after repeated matching failure responses.
- **OWASP Mapping**: Maps to `API4: Unrestricted Resource Consumption`.

## Safety

- The guard reduces traffic after a likely outage or dependency exhaustion.
- It completes the run with stored partial evidence instead of treating the BendIt run itself as failed.
