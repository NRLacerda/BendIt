# API Path Wordlist Discovery

Discovers and verifies API endpoints using path wordlists (native resources or custom input) by performing safe active probing requests.

## Inputs

- **Base URL** (`string`): Target endpoint root URL.
- **Use Native API List** (`boolean`): Configures whether the built-in backend path list (`backend/Resources/api-list.txt`) is used. The native list includes method-prefixed common API routes such as `GET /api/users`, `GET /api/orders/{id}`, and `POST /api/token`.
- **Attached API List** (`[]string`): A list of custom paths loaded from a user-attached `.txt` or `.json` file. The dashboard does not provide a large manual paste field; custom routes enter the run through the file attachment control.
- **Max Workers** (`int`): Maximum execution worker goroutines (defaults to 6).
- **Follow Redirects** (`boolean`): Configuration to determine whether the HTTP client follows redirects.

## Processing

Implementation is isolated under `backend/Discovery/DiscoveryApiList.cs`; `DiscoveryOrchestrator` invokes this step after WebPage JavaScript discovery (when `isWebPage` is true) and OpenAPI discovery, then delegates candidate verification to the shared verification step. Parallelism is limited to that verification step.

1. **Baseline Fingerprinting**:
   - Executes `GET` requests to 3 randomly generated paths (e.g. `/api/__bendit_random_xxx`).
   - Stores baseline statuses, response sizes, and body hashes. This is used to detect soft-404 behaviors (e.g. servers returning 200 OK for missing files).
2. **Path Resolution**:
   - Normalizes lines from the custom and native wordlists.
   - Extracts route method (defaults to `GET` if omitted) and target path.
   - Resolves target paths against the base URL.
3. **Parallel Verification Pipeline**:
   - Enqueues candidate endpoints to the verification pipeline.
   - Runs a pool of parallel workers (up to `MaxWorkers`) processing candidate endpoints:
     - If the HTTP method is unsafe (e.g. `POST`, `PUT`, `DELETE`), skips network probes and immediately registers them as `confirmed` (or `unknown` with low confidence) to avoid modifying state during discovery.
     - If the method is safe (`GET`, `HEAD`, `OPTIONS`), executes a probe request.
4. **Classification & Verification**:
   - Matches the response details against baseline fingerprints to identify soft-404 status codes.
   - Classifies routes:
     - 2xx: `confirmed` or `likely_exists` (confidence 65-80 depending on content-type).
     - 401/403: `protected` (confidence 70).
     - 405: `method_not_allowed` (confidence 65).
     - 404 / baseline match: `soft_404` or `not_found` (confidence 0-20).
     - 5xx: `server_error` (confidence 30).
5. **Registration**:
   - normalizes the route path template (e.g. replaces numeric blocks with `{id}`) and saves validated routes to `endpoints.json`.

## Outputs

- **Discovered Endpoints** (`[]Endpoint`): Array of validated endpoints written to storage, containing classification, confidence, authentication necessity flag (`authRequired`), and redirect endpoints.
