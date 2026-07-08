# HTTP Method Validator

Checks whether an endpoint rejects HTTP methods outside the discovered or expected method.

## Inputs

- **Discovered Endpoint** (`Endpoint`): The method and URL selected by the test battery.
- **Project Config** (`Project`): Base request headers and masked authentication context.
- **Bend Type** (`httpMethodValidation`): Opt-in or default robustness test selected through `bendTypes`.

## Processing

1. **Alternate Method Probes**:
   - Builds a bounded list from `GET`, `POST`, `PUT`, `PATCH`, `DELETE`, and `OPTIONS`.
   - Skips the endpoint's discovered method.
   - Sends at most four alternate method probes to avoid aggressive method fuzzing.
   - Adds a small JSON body only for body-capable alternate methods.
2. **Risk Evaluation**:
   - Low risk when alternate methods return expected blocking responses such as `405`, `404`, `401`, or `403`.
   - Medium risk when alternate method probes cause server errors.
   - High risk when any alternate method returns a 2xx response.

## Outputs

- **Test Result** (`TestResult`): One aggregated result using the highest-risk alternate method response.
- **Mutation Metadata**: Stores `type=httpMethodProbe`, expected method, probed methods, status codes, and whether each was accepted.
- **Evidence**: Summarizes accepted unexpected methods or confirms alternate methods were rejected.
- **OWASP Mapping**: Maps to `API5: Broken Function Level Authorization`.

## Safety

- Probe count is bounded.
- Payloads are small and static.
- No destructive route-specific semantics are inferred; BendIt only records whether the server accepted alternate methods.
