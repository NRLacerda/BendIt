# Content-Type Enforcement Validator

Validates whether JSON-capable endpoints reject ambiguous or incorrect request media types.

## Inputs

- **Discovered Endpoint** (`Endpoint`): A body-capable endpoint selected by the test battery.
- **Project Config** (`Project`): Base request headers and masked authentication context.
- **Bend Type** (`contentTypeValidation`): Opt-in robustness test selected through `TestRunRequest.bendTypes`.

## Processing

1. **Baseline JSON Request**:
   - Sends a syntactically valid JSON payload with `Content-Type: application/json`.
   - Uses the same timeout, bounded response capture, project headers, and auth handling as other robustness tests.
2. **Wrong Media Type Probe**:
   - Sends the same JSON body with `Content-Type: text/plain`.
   - Records whether the endpoint rejects the request with `400`, `415`, or `422`, or accepts it with a `2xx` response.
3. **Missing Media Type Probe**:
   - Sends the same JSON body with no `Content-Type` header.
   - Records whether the endpoint rejects or accepts the ambiguous request.
4. **Risk Evaluation**:
   - Low risk when the valid JSON baseline succeeds and both invalid variants are rejected.
   - Medium risk when one invalid variant is accepted.
   - High risk when both invalid variants are accepted.
   - Low/inconclusive when the valid JSON baseline does not succeed, because enforcement cannot be judged reliably.

## Outputs

- **Test Result** (`TestResult`): One aggregated result for the endpoint, using the most relevant invalid variant response as representative evidence.
- **Mutation Metadata**: Stores `type=contentTypeEnforcement`, baseline status, `text/plain` status, missing-content-type status, accepted invalid variants, rejected invalid variants, and whether the baseline was accepted.
- **Evidence**: Explicitly states whether `text/plain` JSON and missing `Content-Type` JSON were accepted or rejected.
- **OWASP Mapping**: Maps to `API8: Security Misconfiguration`.

## Safety

- Runs only for methods that support request bodies.
- Sends small static JSON payloads only.
- Does not run by default unless `contentTypeValidation` is selected in `bendTypes`.
