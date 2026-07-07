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
   - Executes the endpoint without authentication for `authConsistency`.
   - Flags protected endpoints that still return 2xx.
2. **Malformed Auth Probe**:
   - Sends deterministic invalid bearer or cookie values for JWT/cookie tests.
   - Does not persist or require raw user secrets.
3. **Risk Assignment**:
   - Treats 2xx responses on protected endpoints as high-risk authentication failures.
   - Stores request headers masked in the result evidence.

## Outputs

- **Authentication Boundary Results**: OWASP API2-mapped results showing whether protected endpoints reject missing or malformed authentication.
