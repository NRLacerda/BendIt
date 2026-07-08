# Error Disclosure Validator

Triggers safe malformed request scenarios and checks whether API responses reveal implementation details.

## Inputs

- **Discovered Endpoint** (`Endpoint`): A body-capable endpoint selected by the test battery.
- **Project Config** (`Project`): Base request headers and masked authentication context.
- **Bend Type** (`errorDisclosure`): Opt-in robustness test selected through `TestRunRequest.bendTypes`.

## Processing

1. **Safe Error Probes**:
   - Sends malformed JSON.
   - Sends an empty JSON object.
   - Sends wrong-type fields such as numeric strings and invalid dates.
   - Sends internal-looking fields such as `tenantId`, `ownerId`, `isAdmin`, and `role`.
2. **Disclosure Detection**:
   - Looks for exception names, stack trace markers, file paths, framework/runtime names, SQL/database errors, internal dependency failures, and internal field hints.
   - Treats ordinary public validation messages as low risk when no implementation disclosure is present.
3. **Risk Evaluation**:
   - Low risk when responses contain only sanitized validation errors.
   - Medium risk when responses reveal internal fields, verbose model binding, or schema hints.
   - High risk when responses reveal exception classes, stack traces, file paths, framework/runtime names, database failures, connection pool errors, or internal service timeouts.

## Outputs

- **Test Result** (`TestResult`): One aggregated result for the endpoint, using the highest-risk probe response as representative evidence.
- **Mutation Metadata**: Stores `type=errorDisclosureProbe`, probe names, status codes, risk values, disclosure categories, and short matched terms.
- **Evidence**: Summarizes disclosure categories and short matched terms without copying full stack traces or long error bodies.
- **OWASP Mapping**: Maps to `API8: Security Misconfiguration`.

## Safety

- Runs only for methods that support request bodies.
- Uses small static request bodies and bounded response capture.
- Does not run by default unless `errorDisclosure` is selected in `bendTypes`.
