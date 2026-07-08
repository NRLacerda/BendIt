# BendIt Feature Summary

This file groups the current BendIt features by workflow stage and also keeps the planned feature backlog visible for later implementation.

## Discovery

- **WebPage JavaScript discovery**: For `WebPage` targets, BendIt scans same-origin scripts first and extracts API routes from client-side code.
- **OpenAPI / Swagger discovery**: Probes common documentation routes and loads OpenAPI documents when they are exposed.
- **OpenAPI path extraction**: Normalizes paths and HTTP methods from JSON specs into endpoint candidates.
- **Safe active probing**: Uses `GET`, `HEAD`, and `OPTIONS` to verify candidate routes without aggressive testing.
- **Soft-404 detection**: Compares responses against a baseline to avoid treating generic error pages as real endpoints.
- **Native API list discovery**: Uses the built-in `backend/Resources/api-list.txt` route list to seed discovery.
- **Custom API list attachment**: Accepts a project-specific API list upload from the dashboard and normalizes the entries before storage.
- **Endpoint classification**: Labels candidates as `confirmed`, `likely_exists`, `protected`, `method_not_allowed`, `maybe_exists`, `soft_404`, `rate_limited`, `server_error`, or `unknown` and assigns confidence scores.

## Test Battery

- **Verb-aware test routing**: Only runs the checks that make sense for the endpoint method, so body-oriented tests stay off `GET`, `HEAD`, and `OPTIONS`.
- **Authentication boundary probes**: Sends missing-auth, malformed JWT, and malformed cookie variants without storing raw JWTs, cookies, or API keys.
- **BOLA / IDOR identifier mutation**: Mutates path placeholders, numeric or UUID segments, and identifier-like query parameters to test object-level authorization.
- **Security header validation**: Checks response headers for missing or weak defensive hardening signals.
- **Rate limit validation**: Sends a baseline request, a controlled burst, and a post-burst comparison to detect missing `429`, missing throttling headers, and degraded post-burst behavior.
- **Request size validation**: Expands request payloads to test size limits and parser resilience.
- **Field size validation**: Expands individual fields to probe validation and parser boundaries.
- **Mass assignment testing**: Adds privileged or unexpected JSON fields to check whether the API accepts them.
- **Content-type validation**: Sends valid JSON with `application/json`, `text/plain`, and missing `Content-Type` variants to verify that body-capable endpoints reject ambiguous or wrong media types.
- **Error disclosure validation**: Sends safe malformed request probes and flags verbose validation, stack traces, framework details, database errors, connection pool failures, and internal field hints.
- **Parameter pollution validation**: Sends duplicated query, form, and JSON parameters to detect unsafe duplicate-key collapsing, privileged value override, and ambiguous object-selection behavior.
- **CORS validation**: Sends attacker-origin actual and preflight probes to detect wildcard origins, reflected origins, credentialed cross-origin access, and broad sensitive-header preflight approval.
- **HTTP method validation**: Sends bounded alternate-method probes and flags endpoints that accept unexpected verbs.
- **Response diffing**: Sends repeated identical requests and compares status, content type, body size class, and body hash for unstable behavior.
- **Inventory exposure detection**: Flags live legacy, versioned, internal, debug, documentation, and operational routes.
- **Sensitive data exposure detection**: Reports data classes found in bounded responses without copying the actual secret values into evidence.

## Results

- **OWASP mapping**: Each result is assigned an OWASP API Security Top 10 category.
- **Remediation guidance**: Each result includes a short recommendation tied to the observed issue.
- **Risk scoring**: Results are scored from `0` to `10` and labeled by severity.
- **Evidence capture**: Stores the request URL, response status, masked headers, bounded body, and human-readable evidence.
- **Finding classification**: Marks results as interesting or suspicious when the observed behavior looks risky or degraded.

## Storage

- **Local JSON artifacts**: Writes project data, endpoint inventory, current run state, and result documents to `bend-results/`.
- **Run history**: Keeps per-run JSON artifacts so past audits can be reviewed and downloaded later.
- **Project metadata**: Stores project ID, base URL, target type, description, headers, and masked auth context.

## Dashboard

- **Project list workflow**: The UI centers on selecting a project, starting a run, and reviewing stored results.
- **Live execution page**: Shows run progress, result counts, and finding counts while the backend is still working.
- **Endpoint drill-down**: Groups results by endpoint and shows per-test evidence for quick review.
- **Result downloads**: Exposes stored run artifacts for offline inspection.

## Safety

- **Authorized testing only**: BendIt is intended for systems you own or are explicitly allowed to test.
- **Bounded capture**: Response bodies are capped at 100 KB per result and requests time out after 5 seconds.
- **Controlled mutations**: The runner avoids pretending unsafe tests were run when a method does not support them.
- **Masked secrets**: Authentication material is persisted only as masked metadata.

## Planned Features

- **Function-level authorization heuristics**: Detect routes like `/admin`, `/manage`, `/export`, or similar privileged paths and verify whether they are reachable without proper auth. Maps to OWASP API5.
- **Timing and error differential analysis**: Compare baseline and mutated responses to detect enumeration or inconsistent auth failures. Maps to API1, API2, or API9 depending the signal.
- **SSRF-safe URL field validation**: Probe URL-looking fields with safe invalid or documentation-reserved hosts only, then detect fetch attempts or fetch-related errors. Maps to API10.
- **GraphQL discovery and validation**: Detect `/graphql`, introspection exposure, verbose errors, and basic query depth or field errors behind a detected GraphQL route.
- **Report export**: Add Markdown, HTML, or SARIF-style exports from run results for offline review and CI workflows.
