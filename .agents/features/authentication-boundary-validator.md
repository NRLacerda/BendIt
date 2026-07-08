# Authentication Boundary Validator


Checks whether endpoints that appear protected reject missing or malformed authentication without requiring BendIt to persist raw credentials.

## Inputs

- **Endpoint Auth Flag** (`authRequired`): Discovery classification indicating that an endpoint likely requires authentication.
- **Project Auth Type** (`AuthConfig.type`): Selected auth mode (`jwt`, `cookie`, `headers`, or `none`), stored only as masked metadata.
- **Bend Types**:
  - `authConsistency`: Sends no auth context and verifies protected endpoints do not return success.
  - `jwtAnalysis`: Sends a malformed bearer token when JWT auth is configured.
  - `cookieAnalysis`: Sends a malformed cookie when cookie auth is configured.

## Processing

1. **No-Auth Probe**:
   - Executes the endpoint with common auth headers removed for `authConsistency`.
   - Flags protected endpoints that still return 2xx.
2. **Malformed JWT Probes**:
   - Sends deterministic malformed, unsigned-shape, and empty bearer token probes for `jwtAnalysis`.
   - Masks the `Authorization` header in stored request evidence.
3. **Malformed Cookie Probes**:
   - Sends invalid, empty, and duplicated session cookie probes for `cookieAnalysis`.
   - Masks the `Cookie` header in stored request evidence.
4. **Secret Handling**:
   - Does not persist or require raw user secrets.
5. **Risk Assignment**:
   - Treats 2xx responses on protected endpoints as high-risk authentication failures.
   - Treats parser/server errors as suspicious because malformed credentials should not crash auth handling.
   - Stores request headers masked in the result evidence.

## Outputs

- **Authentication Boundary Results**: OWASP API2-mapped results showing whether protected endpoints reject missing or malformed authentication.
