# BOLA Identifier Mutation

Expands generic object-level authorization checks by detecting mutable identifiers beyond explicit `{id}` path templates.

## Inputs

- **Endpoint Path** (`Endpoint.path`): Route path that may contain placeholders, numeric IDs, UUID-like segments, or static paths.
- **Query Parameters** (`Endpoint.queryParams`): Discovered query parameter names.
- **Bend Type** (`idMutation`): Authorization-oriented mutation used to probe BOLA/IDOR behavior.

## Processing

1. **Identifier Detection**:
   - Runs `idMutation` when the path contains route placeholders.
   - Also runs when path segments contain numeric or UUID-like identifiers.
   - Also runs when query parameter names look object-related, such as `id`, `userId`, `accountId`, `tenantId`, `ownerId`, or `resourceId`.
2. **Mutation Construction**:
   - Replaces path placeholders or identifier-looking path segments with deterministic alternate values.
   - Adds low-risk query values for identifier-like query parameters when available.
3. **Risk Assignment**:
   - Treats successful responses from mutated protected/object-looking endpoints as BOLA risk.
   - Keeps the check generic and bounded; it does not brute-force identifiers.

## Outputs

- **BOLA Results**: `idMutation` results with URLs showing mutated path/query identifiers and OWASP API1 mapping.
