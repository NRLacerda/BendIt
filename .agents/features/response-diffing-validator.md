# Response Diffing Validator

Compares repeated responses from the same endpoint to identify unstable or inconsistent behavior.

## Inputs

- **Discovered Endpoint** (`Endpoint`): The endpoint selected by the test battery.
- **Project Config** (`Project`): Base request headers and masked authentication context.
- **Bend Type** (`responseDiffing`): Robustness test selected through `bendTypes`.

## Processing

1. **Repeated Request Pair**:
   - Sends two identical requests to the concrete endpoint URL.
   - Uses bounded response capture and the same timeout as other robustness tests.
2. **Comparison Signals**:
   - Compares status code.
   - Compares response content type.
   - Compares response body size class.
   - Compares a short hash of the bounded response body.
3. **Risk Evaluation**:
   - Low risk when repeated responses are stable.
   - Low/informational risk when only minor non-status differences are observed.
   - Medium risk when status changes or either response returns a server error.

## Outputs

- **Test Result** (`TestResult`): One aggregated comparison result.
- **Mutation Metadata**: Stores `type=repeatResponseComparison`, both status codes, both body sizes, and comparison signals.
- **Evidence**: States whether repeated responses were stable or which signals changed.
- **OWASP Mapping**: Maps to `API9: Improper Inventory Management`.

## Safety

- Sends only two sequential requests.
- Does not mutate path identifiers or request bodies.
- Does not copy more than the bounded response body already captured by the test battery.
