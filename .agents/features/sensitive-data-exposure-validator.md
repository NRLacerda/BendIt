# Sensitive Data Exposure Validator

Detects common sensitive data classes in API responses and maps them to OWASP API3 when endpoints appear to return fields that should be minimized or protected.

## Inputs

- **Discovered Endpoints** (`[]Endpoint`): Testable endpoints selected by the battery.
- **HTTP Response Evidence**: Bounded response body, response content type, and masked response headers from real requests.
- **Bend Type** (`sensitiveDataExposure`): Safe response-only validation that does not mutate application state.

## Processing

1. **Response Inspection**:
   - Checks bounded response bodies for credential-shaped fields such as tokens, API keys, secrets, passwords, and session identifiers.
   - Checks for common personal data markers such as email addresses, phone-like values, and national identifier field names.
   - Reports only data classes and field names; it does not copy detected secret values into evidence.
2. **Risk Assignment**:
   - Treats credential-shaped exposure as high risk.
   - Treats personal data exposure as medium risk requiring review.
   - Keeps responses without sensitive indicators as low-risk raw evidence.
3. **OWASP Mapping**:
   - Emits `API3: Broken Object Property Level Authorization` because excessive or sensitive response properties should be controlled per object and per caller.

## Outputs

- **Sensitive Data Findings**: `TestResult` entries with `bendType = sensitiveDataExposure`, evidence describing detected data classes, OWASP category, risk score, and remediation guidance.
