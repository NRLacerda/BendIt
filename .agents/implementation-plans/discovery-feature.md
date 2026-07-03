# BendIt API Discovery Implementation Plan

## Goal

Implement the first discovery module for BendIt, focused on:

1. OpenAPI / Swagger discovery
2. Endpoint extraction from OpenAPI specs
3. JavaScript route extraction
4. `robots.txt` and `sitemap.xml` discovery
5. Framework-specific default route discovery
6. Curated API wordlist discovery
7. Endpoint verification and classification
8. Storage of discovered endpoints for later robustness testing

The tool must be designed for authorized testing only and should use conservative defaults.

---

# 1. Discovery Pipeline

The discovery process should be mostly linear, with parallelism inside each stage.

```text
Project Target
    ↓
Baseline fingerprint
    ↓
OpenAPI / Swagger discovery
    ↓
OpenAPI endpoint extraction
    ↓
robots.txt / sitemap.xml discovery
    ↓
HTML / JavaScript asset discovery
    ↓
JavaScript route extraction
    ↓
Framework-specific route checks
    ↓
Curated API wordlist discovery
    ↓
Endpoint verification
    ↓
Endpoint inventory
    ↓
Next testing phase
```

Each stage should emit endpoint candidates into a shared normalized endpoint store.

---

# 2. Main Design Principle

Do not immediately “attack” discovered endpoints.

Discovery should only answer:

```text
Does this endpoint probably exist?
Which HTTP methods are likely valid?
Where was it discovered from?
How confident are we?
Does it require authentication?
```

---

# 3. Endpoint Data Model

Create an internal endpoint candidate model.

```go
type EndpointCandidate struct {
    ID              string
    BaseURL         string
    Path            string
    Method          string
    NormalizedKey   string

    Source          string
    SourceDetail    string

    StatusCode      int
    ContentType     string
    ResponseLength  int64
    ResponseHash    string

    Confidence      int
    Classification  string

    AuthRequired    bool
    Protected       bool
    RedirectedTo    string

    FirstSeenAt     time.Time
    LastSeenAt      time.Time
}
```

Example `Source` values:

```text
openapi
swagger-ui
robots
sitemap
javascript
framework-default
wordlist
observed-browser-traffic
manual
```

Example `Classification` values:

```text
confirmed
likely_exists
protected
method_not_allowed
maybe_exists
not_found
soft_404
rate_limited
server_error
unknown
```

---

# 4. Endpoint Normalization

Before storing any endpoint, normalize it.

Rules:

```text
Remove duplicate slashes
Remove trailing slash except root
Lowercase scheme and host
Keep path case as-is
Remove query string for route identity
Convert numeric IDs to placeholders when useful
Deduplicate method + normalized path
```

Examples:

```text
/api/users/123      -> /api/users/{id}
/api/users/999      -> /api/users/{id}
/api/orders/abc123  -> /api/orders/{value}
```

Normalized key:

```text
METHOD + " " + normalized_path
```

Example:

```text
GET /api/users/{id}
```

This prevents processing the same logical endpoint multiple times.

---

# 5. Baseline Fingerprinting

Before testing real routes, request random fake paths.

Example:

```text
/__bendit_random_928371
/api/__bendit_random_928371
/random-not-found-bendit-928371
```

Record:

```text
status code
content length
body hash
title
content type
response shape
redirect behavior
```

Purpose:

Detect:

```text
normal 404 behavior
soft 404 behavior
SPA fallback behavior
default error page
default JSON error format
```

Example problem:

```text
GET /fake-route returns 200 with index.html
```

This means `200` alone cannot prove an endpoint exists.

---

# 6. Stage 1 — OpenAPI / Swagger Discovery

Check documentation/specification routes first.

Initial route list:

```text
/openapi.json
/openapi.yaml
/openapi.yml

/swagger
/swagger/
/swagger.json
/swagger.yaml
/swagger/v1/swagger.json
/swagger/v2/swagger.json
/swagger/index.html

/swagger-ui
/swagger-ui/
/swagger-ui.html
/swagger-ui/index.html

/api-docs
/api-docs/
/v2/api-docs
/v3/api-docs
/v3/api-docs/
/v3/api-docs/swagger-config

/docs
/docs/
/redoc
/redoc/
/rapidoc
/rapidoc/

/api
/api-json
/docs-json
```

Use only safe methods:

```text
GET
HEAD
OPTIONS
```

## OpenAPI Detection

If the response is JSON or YAML, check for:

```text
openapi
swagger
info
paths
components
schemas
definitions
securitySchemes
```

If valid OpenAPI or Swagger spec is found:

1. Parse the spec
2. Extract `servers`
3. Extract `paths`
4. Extract methods
5. Extract request schemas
6. Extract response schemas
7. Queue all discovered endpoints

Example output:

```json
{
  "path": "/users/{id}",
  "method": "GET",
  "source": "openapi",
  "confidence": 100
}
```

## Swagger UI Detection

For HTML pages, search for:

```text
swagger-ui-bundle
swagger-ui.css
SwaggerUIBundle
url:
urls:
configUrl
/v3/api-docs
/swagger.json
```

If Swagger UI references a spec URL, request that spec URL and parse it.

---

# 7. Stage 2 — robots.txt and sitemap.xml

Check:

```text
/robots.txt
/sitemap.xml
/sitemap_index.xml
```

From `robots.txt`, parse:

```text
Allow
Disallow
Sitemap
```

From `sitemap.xml`, parse:

```text
loc
```

Rules:

```text
Do not assume Disallow means vulnerability.
Do not aggressively test disallowed paths by default.
Add paths as low-confidence candidates unless they look like API routes.
```

Useful API-like patterns:

```text
/api/
/graphql
/admin
/auth
/oauth
/users
```

---

# 8. Stage 3 — HTML and JavaScript Asset Discovery

This stage requires either:

1. Browser discovery through Playwright, or
2. Static fetch of the base HTML page.

Preferred approach:

```text
Launch browser → user logs in manually → capture loaded assets and API traffic
```

Collect:

```text
HTML pages
script src URLs
JavaScript bundles
source maps if available
observed API calls
```

JavaScript files to analyze:

```text
*.js
*.mjs
*.chunk.js
*.bundle.js
```

Optionally analyze source maps:

```text
*.map
```

But mark exposed source maps as a finding.

---

# 9. Stage 4 — JavaScript Route Extraction

Parse JS content for API route patterns.

Search patterns:

```text
fetch("/api/...")
fetch('/api/...')
axios.get("/api/...")
axios.post("/api/...")
http.get("/api/...")
client.get("/api/...")
baseURL:
apiUrl
apiBaseUrl
VITE_API_URL
NEXT_PUBLIC_API_URL
REACT_APP_API_URL
```

Also search for route-like strings:

```text
/api/
/auth/
/login
/logout
/users
/accounts
/orders
/products
/graphql
```

Extract possible:

```text
base URLs
relative paths
HTTP methods
GraphQL endpoints
auth endpoints
```

Important: JavaScript route extraction can produce false positives.

Store with medium or low confidence:

```text
Source: javascript
Confidence: 40-80 depending on evidence
```

Higher confidence if the JS call includes an HTTP method.

Example:

```js
axios.post("/api/users", data)
```

Should produce:

```text
POST /api/users
confidence: 85
```

Example:

```js
"/api/users"
```

Should produce:

```text
UNKNOWN /api/users
confidence: 50
```

---

# 10. Stage 5 — Framework-Specific Default Routes

Use a small curated list based on common frameworks.

Do not use massive lists by default.

## Spring Boot

```text
/actuator
/actuator/health
/actuator/info
/actuator/metrics
/actuator/prometheus
/v3/api-docs
/swagger-ui/index.html
/swagger-ui.html
```

## ASP.NET Core

```text
/swagger
/swagger/index.html
/swagger/v1/swagger.json
/health
/healthz
/ready
/live
/metrics
```

## Express / NestJS

```text
/api
/docs
/api-docs
/swagger
/swagger-ui
/health
/healthcheck
/metrics
/api-json
/docs-json
```

## FastAPI

```text
/docs
/redoc
/openapi.json
/health
/metrics
```

## Django / DRF

```text
/admin
/api-auth/login
/api-auth/logout
/swagger
/redoc
/schema
/schema/swagger-ui
/schema/redoc
```

## Laravel

```text
/up
/health
/telescope
/horizon
/pulse
/api/documentation
/docs
```

## Rails

```text
/up
/health
/rails/info/routes
/rails/info/properties
```

Classification rules:

```text
200 -> likely exposed
401/403 -> exists but protected
404 -> not found
405 -> route exists but wrong method
3xx -> likely exists, record redirect
```

---

# 11. Stage 6 — Curated API Wordlist Discovery

Use a default `api-list.txt` for common API routes.

Example entries:

```text
/api
/api/v1
/api/v2
/auth
/auth/login
/auth/logout
/users
/users/me
/profile
/accounts
/orders
/products
/search
/admin
/health
/status
/metrics
/graphql
```

Sources for optional user-provided wordlists:

```text
SecLists
Assetnote wordlists
fuzzdb
PayloadsAllTheThings
Kiterunner route lists
```

Important:

```text
Do not bundle huge aggressive wordlists as the default behavior.
Default BendIt mode should be small and conservative.
Allow advanced users to import their own wordlists.
```

---

# 12. Wordlist Processing Design

Do not split work manually by line ranges.

Prefer a worker pool.

Reason:

```text
Some endpoints respond fast.
Some endpoints timeout.
Some endpoints redirect.
Some endpoints are rate limited.
Fixed line ranges can create unbalanced workers.
```

Use this model:

```text
Reader goroutine
    ↓
jobs channel
    ↓
N worker goroutines
    ↓
results channel
    ↓
single writer / store goroutine
```

This gives:

```text
parallel requests
centralized deduplication
safe writes
backpressure
controlled concurrency
```

---

# 13. Worker Pool Design

## Components

```go
type DiscoveryJob struct {
    Method string
    Path   string
    Source string
}

type DiscoveryResult struct {
    Candidate EndpointCandidate
    Error     error
}
```

## Flow

```text
1. Read api-list.txt line by line
2. Normalize candidate path
3. Skip if already scheduled
4. Send job to jobs channel
5. Workers execute HTTP request
6. Workers classify response
7. Results go to results channel
8. Store goroutine deduplicates and persists
```

## Concurrency Controls

Use:

```text
maxWorkers
requestsPerSecond
requestTimeout
maxRedirects
maxRetries
```

Recommended defaults:

```yaml
discovery:
  maxWorkers: 10
  requestsPerSecond: 5
  timeoutSeconds: 10
  maxRetries: 1
  followRedirects: false
```

---

# 14. Deduplication Strategy

Use a central deduplication map or database unique constraint.

Key:

```text
base_url + method + normalized_path
```

For unknown methods:

```text
base_url + "UNKNOWN" + normalized_path
```

When a better method is discovered later, update the record.

Example:

```text
UNKNOWN /api/users from JS
GET /api/users from OpenAPI
POST /api/users from OpenAPI
```

Final records:

```text
GET /api/users
POST /api/users
```

Optionally keep the original unknown route as metadata.

---

# 15. Response Classification

Classification table:

```text
200, 201, 202, 204:
  likely_exists or confirmed

301, 302, 307, 308:
  likely_exists_redirect

400:
  maybe_exists_bad_request

401:
  protected_auth_required

403:
  protected_forbidden

404:
  not_found

405:
  method_not_allowed_route_exists

408, timeout:
  timeout_unknown

429:
  rate_limited

500-599:
  server_error_possible_route
```

Do not blindly treat `400` as found.

A bad request can mean:

```text
real endpoint but missing params
API gateway rejected request
default backend error
WAF/proxy response
```

Assign lower confidence.

---

# 16. Soft 404 Detection

Compare candidate responses against baseline fake-path responses.

Use:

```text
status code
content length
body hash
title
content type
similarity score
redirect target
```

If candidate response looks like the fake route response:

```text
classification: soft_404
confidence: low
```

Example:

```text
GET /api/users -> 200 index.html
GET /fake-random -> 200 index.html
```

This should not be considered a confirmed API endpoint.

---

# 17. HTTP Method Discovery

For each confirmed or likely endpoint, determine possible methods safely.

Order:

```text
1. Use methods from OpenAPI if available
2. Use methods observed in browser traffic
3. Use method from JS extraction if available
4. Use OPTIONS response Allow header if available
5. Use conservative method probing
```

Safe method probing:

```text
HEAD
GET
OPTIONS
```

Do not automatically test destructive methods during discovery:

```text
POST
PUT
PATCH
DELETE
```

Those should be deferred to the robustness testing phase and require approval.

---

# 18. Storage Strategy

For MVP, use SQLite.

Tables:

```text
projects
discovery_runs
endpoint_candidates
endpoint_evidence
openapi_specs
js_assets
framework_hints
```

## endpoint_candidates

Fields:

```text
id
project_id
base_url
path
normalized_path
method
normalized_key
source
status_code
content_type
response_length
response_hash
classification
confidence
auth_required
protected
redirected_to
first_seen_at
last_seen_at
```

## endpoint_evidence

Fields:

```text
id
endpoint_id
source
source_detail
request_sample_redacted
response_sample_redacted
evidence_text
created_at
```

Use database unique constraints to avoid duplicates.

Example:

```text
UNIQUE(project_id, base_url, method, normalized_path)
```

---

# 19. In-Memory vs File Writing

Avoid writing directly to files from many goroutines.

Recommended:

```text
Workers produce results
Single store goroutine writes to SQLite
Report/export happens after the run
```

For CSV/JSON output:

```text
Read from SQLite sequentially
Generate files after discovery completes
```

This avoids file corruption and simplifies concurrency.

Your idea of batching results in memory is okay for small wordlists, but SQLite is safer and gives resumability.

---

# 20. Batch Processing

Batches are still useful, but not as the main concurrency model.

Use batches for:

```text
UI progress updates
database bulk inserts
report generation
retry scheduling
large wordlist progress tracking
```

Example:

```text
Every 100 results:
  persist batch
  emit progress event to dashboard
```

But do not assign fixed line ranges to workers unless there is a strong reason.

---

# 21. Progress Tracking

Track:

```text
total candidates loaded
jobs queued
jobs processed
confirmed endpoints
protected endpoints
not found
soft 404
errors
rate limited
current requests per second
estimated time remaining
```

Dashboard should show:

```text
Discovery stage
Progress %
Found endpoints
Current rate
Errors
Pause/stop button
```

---

# 22. Safety Controls

Default discovery must be conservative.

Required config:

```yaml
discovery:
  maxWorkers: 10
  requestsPerSecond: 5
  timeoutSeconds: 10
  maxRetries: 1
  methods:
    - GET
    - HEAD
    - OPTIONS
  followRedirects: false
  maxWordlistEntries: 5000
  respectRobotsTxt: false
```

Add user-facing warnings for:

```text
large wordlists
high concurrency
testing outside declared scope
POST/PUT/PATCH/DELETE discovery
```

Require explicit approval for:

```text
destructive methods
large wordlists
authenticated testing
rate-limit testing
payload fuzzing
```

---

# 23. Configuration Example

```yaml
project:
  name: "Local Test API"
  baseUrl: "http://localhost:3000"

discovery:
  openapi: true
  swagger: true
  robots: true
  sitemap: true
  javascript: true
  frameworkDefaults: true
  wordlist: true

  wordlistPath: "./wordlists/api-list.txt"
  maxWordlistEntries: 5000

  maxWorkers: 10
  requestsPerSecond: 5
  timeoutSeconds: 10
  maxRetries: 1
  followRedirects: false

  methods:
    - GET
    - HEAD
    - OPTIONS

scope:
  include:
    - "http://localhost:3000/**"
  exclude:
    - "/payments/**"
    - "/delete/**"
    - "/admin/delete/**"

safety:
  requireScopeApproval: true
  allowDestructiveMethods: false
  redactSecrets: true
```

---

# 24. Suggested Go Package Structure

```text
internal/
  discovery/
    runner.go
    pipeline.go
    baseline.go
    classifier.go
    normalize.go
    dedupe.go

    openapi/
      discover.go
      parse.go
      swagger_ui.go

    robots/
      robots.go
      sitemap.go

    javascript/
      assets.go
      extract_routes.go
      patterns.go

    framework/
      defaults.go
      fingerprints.go

    wordlist/
      reader.go
      worker_pool.go

  httpclient/
    client.go
    rate_limiter.go
    redirects.go

  storage/
    sqlite.go
    endpoints.go
    evidence.go

  models/
    endpoint.go
    discovery.go
    finding.go

  server/
    routes.go
    discovery_handlers.go
    sse.go
```

---

# 25. Discovery Runner Interface

Create a single orchestrator.

```go
type DiscoveryRunner struct {
    ProjectID string
    BaseURL   string
    Config    DiscoveryConfig
    Store     DiscoveryStore
    Client    HTTPClient
}

func (r *DiscoveryRunner) Run(ctx context.Context) error {
    // 1. Baseline
    // 2. OpenAPI/Swagger
    // 3. Robots/Sitemap
    // 4. JavaScript
    // 5. Framework defaults
    // 6. Wordlist
    // 7. Final verification/classification
    return nil
}
```

---

# 26. Discovery Store Interface

```go
type DiscoveryStore interface {
    SaveEndpoint(ctx context.Context, endpoint EndpointCandidate) error
    SaveEvidence(ctx context.Context, endpointID string, evidence EndpointEvidence) error
    EndpointExists(ctx context.Context, projectID, method, normalizedPath string) (bool, error)
    ListEndpoints(ctx context.Context, projectID string) ([]EndpointCandidate, error)
}
```

Use SQLite implementation first.

---

# 27. HTTP Client Requirements

Implement a shared HTTP client with:

```text
timeout
rate limiting
redirect policy
headers
cookie/session support
auth header support
user agent
response body max read size
```

Important:

```text
Do not read unlimited response bodies into memory.
```

Default max body read for discovery:

```text
1 MB
```

For hashing/classification, only partial body is needed.

---

# 28. Request Execution Result

```go
type HTTPResult struct {
    URL             string
    Method          string
    StatusCode      int
    Headers         http.Header
    BodySample      []byte
    BodyHash        string
    ContentLength   int64
    Duration        time.Duration
    RedirectedTo    string
    Error           error
}
```

---

# 29. Confidence Scoring

Suggested rules:

```text
OpenAPI endpoint: +100
Observed browser request: +95
Swagger UI linked spec: +90
JS method call found: +80
Known framework route 200: +80
Known framework route 401/403: +70
Wordlist 200 JSON response: +70
Wordlist 405: +65
Wordlist 401/403: +65
Wordlist 400: +40
Soft 404 detected: cap at 20
HTML SPA fallback: cap at 25
```

Example:

```go
func ScoreEndpoint(result HTTPResult, source string, baseline Baseline) int {
    // Apply status code, source, content-type, and soft-404 rules.
}
```

---

# 30. Discovery Output

At the end of discovery, BendIt should produce:

```text
Endpoint inventory
OpenAPI specs found
Protected documentation routes
Framework hints
JS-extracted routes
robots/sitemap routes
Default routes found
Soft 404 behavior
Discovery warnings
```

Example endpoint inventory:

```text
GET     /api/users              openapi        confirmed       100
POST    /api/users              openapi        confirmed       100
GET     /api/users/{id}         javascript     likely_exists   85
UNKNOWN /api/profile            javascript     maybe_exists    55
GET     /actuator/health        framework      protected       70
```

---

# 31. Important Adjustments to Original Idea

## Keep

The original idea is good in these parts:

```text
OpenAPI first
Queue discovered endpoints
Use default API list after documentation discovery
Store endpoint + HTTP method
Run payload robustness tests later
Parallelize inside each step
Avoid duplicate processing
Write results sequentially
```

## Change

Change this:

```text
Split file by fixed line ranges and assign each range to a goroutine.
```

To this:

```text
Use a worker pool with jobs/results channels.
```

Reason:

```text
Better load balancing
Simpler cancellation
Easier rate limiting
Better retry handling
Cleaner progress tracking
```

## Change

Change this:

```text
Store all responses in memory and write files by batch.
```

To this:

```text
Store endpoint metadata and small redacted evidence samples in SQLite.
Generate CSV/JSON files after the run.
```

Reason:

```text
Safer
Resumable
Less memory usage
Better UI querying
No file corruption risk
```

## Change

Change this:

```text
After finding endpoints, immediately POST malformed data.
```

To this:

```text
Discovery creates inventory.
Robustness testing .
```

Reason:

```text
Avoid accidental destructive actions
Cleaner architecture
Better reporting
Safer default behavior
```

---

# 32. First Codex Task

Implement the first vertical slice:

```text
Given a base URL and a small built-in OpenAPI route list,
discover OpenAPI/Swagger endpoints,
parse any found OpenAPI JSON,
extract endpoint paths and methods,
store them in SQLite,
and expose them in the React dashboard.
```

Acceptance criteria:

```text
1. User can create a project with a base URL.
2. User can start discovery.
3. Backend checks common OpenAPI/Swagger routes.
4. Backend detects valid OpenAPI JSON.
5. Backend parses paths and methods.
6. Backend stores endpoints in SQLite.
7. Dashboard lists discovered endpoints.
8. User can export endpoint inventory as CSV.
9. Secrets and auth headers are not printed in logs.
10. Discovery uses safe methods only: GET, HEAD, OPTIONS.
```

---

# 33. Second Codex Task

Implement framework default route discovery.

Acceptance criteria:

```text
1. Backend checks curated framework route lists.
2. Backend classifies responses.
3. Backend detects protected routes with 401/403.
4. Backend detects method-not-allowed route existence with 405.
5. Backend compares responses against soft-404 baseline.
6. Dashboard shows framework hints with confidence score.
7. Endpoint inventory includes source and confidence.
```

---

# 34. Third Codex Task

Implement wordlist worker pool discovery.

Acceptance criteria:

```text
1. Backend loads api-list.txt line by line.
2. Backend normalizes candidate paths.
3. Backend deduplicates scheduled jobs.
4. Backend processes jobs with a configurable worker pool.
5. Backend applies request rate limiting.
6. Backend classifies responses.
7. Backend writes results through a single storage layer.
8. Dashboard shows progress.
9. User can pause/cancel discovery.
```

---

# 35. Fourth Codex Task

Implement JavaScript extraction.

Acceptance criteria:

```text
1. Backend fetches base HTML page.
2. Backend extracts script src URLs.
3. Backend downloads same-origin JS assets.
4. Backend extracts route-like strings.
5. Backend extracts fetch/axios method calls.
6. Backend stores candidates with source javascript.
7. Backend assigns lower confidence to weak string matches.
8. Dashboard shows JS-discovered routes separately.
```

---

# 36. Final Recommendation

Build discovery in this order:

```text
1. OpenAPI / Swagger discovery
2. OpenAPI endpoint extraction
3. Soft-404 baseline
4. Framework default routes
5. Wordlist worker pool
6. robots.txt / sitemap.xml
7. JavaScript static extraction
8. Playwright authenticated discovery
```

The best first MVP is:

```text
OpenAPI discovery + endpoint inventory + SQLite storage + React display + CSV export
```

This gives useful value immediately and becomes the foundation for all later robustness testing.
