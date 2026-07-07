# Security Misconfiguration Validator

Checks generic HTTP response hardening signals that are broadly applicable to WebAPIs and maps them to OWASP API8.

## Inputs

- **Discovered Endpoints** (`[]Endpoint`): Testable endpoints selected by the battery.
- **HTTP Response Headers** (`Dictionary<string,string>`): Headers captured during real test requests.
- **Bend Type** (`securityHeaders`): Safe, response-only validation that does not mutate application state.

## Processing

1. **Header Capture**:
   - Captures response and content headers for every executed request.
   - Masks `Set-Cookie` values before persisting results.
2. **Generic Hardening Checks**:
   - Flags missing `X-Content-Type-Options`.
   - Flags verbose platform headers such as `Server` and `X-Powered-By`.
   - Flags cookies missing `HttpOnly`, `Secure`, or `SameSite` attributes when cookies are present.
3. **Risk Assignment**:
   - Treats isolated missing hardening headers as low/medium risk.
   - Raises risk when cookies are weakly configured or platform disclosure is present.
4. **OWASP Mapping**:
   - Emits `API8: Security Misconfiguration` with actionable remediation guidance.

## Outputs

- **Security Header Findings**: `TestResult` entries with `bendType = securityHeaders`, captured masked headers, evidence, risk score, OWASP category, and recommendation.
* Rate limit validator real behavior Current rateLimit is mostly status-based. It should send a controlled burst within safe limits and detect missing 429, throttling headers, or degraded behavior.