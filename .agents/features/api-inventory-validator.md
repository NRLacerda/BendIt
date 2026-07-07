# API Inventory Validator

Detects exposed endpoints that usually indicate stale, undocumented, or environment-only API surface and maps the result to OWASP API9.

## Inputs

- **Discovered Endpoints** (`[]Endpoint`): Verified routes from OpenAPI, JavaScript, native lists, and custom API lists.
- **Bend Type** (`inventoryExposure`): Safe response-only validation that does not mutate state.
- **Endpoint Metadata**: Path, source, source detail, classification, confidence, HTTP status, and content type.

## Processing

1. **Inventory Signal Detection**:
   - Flags versioned routes such as `/v1`, `/v2`, or `/api/v3` when they are live.
   - Flags legacy, deprecated, beta, experimental, internal, admin, debug, staging, test, mock, or temporary paths.
   - Flags API documentation and operational exposure routes such as Swagger/OpenAPI specs, actuator, metrics, environment, and GraphQL explorer routes.
2. **Safe Verification**:
   - Reuses the normal HTTP execution path and captures bounded response evidence.
   - Does not submit write payloads or alter identifiers.
3. **Risk Assignment**:
   - Raises risk when inventory signals are reachable with successful `2xx` responses.
   - Keeps blocked or not-found inventory candidates as low-risk raw evidence.
4. **OWASP Mapping**:
   - Emits `API9: Improper Inventory Management` with remediation guidance focused on endpoint inventory, deprecation policy, and environment route exposure.

## Outputs

- **Inventory Findings**: `TestResult` entries with `bendType = inventoryExposure`, evidence describing the detected route signal, OWASP API9 category, risk score, and remediation guidance.
