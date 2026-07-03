# WebPage JavaScript Discovery

Profiles a webpage target by extracting likely API endpoints from the initial HTML and same-origin JavaScript assets before the standard OpenAPI and API-list discovery steps run.

## Inputs

- **Project Target Type** (`isWebPage`): When `true`, JavaScript discovery runs first. When `false`, the project is treated as a WebAPI target and this step is skipped.
- **Base URL** (`string`): Webpage entry URL to fetch and use for relative URL resolution. Unlike WebAPI targets, WebPage targets preserve the configured page path.
- **Max Workers** (`int`): Maximum concurrent JavaScript asset fetches, inherited from discovery config.
- **Max Body Bytes** (`int64`): Maximum body size used for page and asset fetches, capped to 5MB for JavaScript assets.

## Current Processing

Implementation lives in `backend/Discovery/DiscoveryJavaScript.cs` and is invoked by `DiscoveryOrchestrator` before OpenAPI and API-list discovery only for WebPage projects.

1. **Static Entry Fetch**:
   - Performs a browser-like `GET` request against the project base URL.
   - Reads the HTML response with the existing discovery body limit.
2. **JavaScript Asset Collection**:
   - Extracts same-origin `<script src="...">` assets.
   - Extracts same-origin preload/modulepreload script links.
   - Extracts literal dynamic imports from inline HTML.
   - Limits processing to 32 JavaScript assets.
3. **Parallel Asset Processing**:
   - Fetches discovered JavaScript assets concurrently using `MaxWorkers`.
   - Keeps this parallelism scoped inside the JavaScript discovery step.
4. **Endpoint Extraction**:
   - Extracts direct `fetch(...)` URLs.
   - Extracts direct `axios(...)` URLs.
   - Falls back to regex extraction for string literals that look like `/api/...`.
   - Detects nearby `method: "POST"` style config values for direct network calls.
   - Normalizes template placeholders such as `${id}` or `{id}` to concrete probe values before verification.
5. **Handoff to Verification**:
   - Emits `EndpointCandidate` values with source `javascript`.
   - Deduplicates by method and URL.
   - Delegates active verification/classification to the shared discovery verification step.

## Outputs

- **JavaScript Candidates**: Candidate endpoints tagged with source `javascript`, source detail set to the HTML page or script asset URL, and confidence based on extraction context.

## Planned Enhancements

Here is the revised, comprehensive, and language-agnostic implementation plan. I have kept the strong foundational elements of the previous plan and injected missing critical components specifically needed for a security testing application, such as Headless Browser dynamic extraction, aggressive Source Map hunting, Secret extraction, and WAF evasion.

Phase 1: JS Source Collection (Static & Dynamic)
Relying solely on static HTML parsing will miss scripts loaded dynamically by modern SPAs. You need a dual approach.

1.1 Static Entry Point Analysis
HTML Parsing: Fetch the initial URL and parse the DOM.

Extraction Targets:

<script src="..."> tags (inline and external).

<link rel="preload" as="script"> tags.

Dynamic imports (import(...) statements inside inline scripts).

Base64 encoded scripts embedded in the HTML.

1.2 Dynamic Execution (New Addition)
Headless Browser Integration: Use a headless browser (e.g., Playwright, Puppeteer) to render the target page for 3-5 seconds.

Network Interception: Capture all .js files requested by the browser during execution. This catches scripts injected by tag managers or obfuscated loaders.

XHR/Fetch Logging: Passively log any API requests made during the initial page load to seed your endpoint list immediately.

1.3 Aggressive Source Map Hunting (Enhanced)
Source maps (.map files) give you the unminified, original developer code.

Standard checks: Look for //# sourceMappingURL= comments.

Brute-forcing: Automatically append .map to every discovered JS file (e.g., app.bundle.js -> app.bundle.js.map).

Common Paths: Check common default build paths (e.g., /static/js/main.js.map).

1.4 Evasion and Resolution (New Addition)
WAF Mimicry: Ensure all fetches use standard browser headers (User-Agent, Accept, Accept-Encoding).

Relative Pathing: Convert relative paths to absolute URLs based on the origin base URL. Track and follow redirects cleanly.

Phase 2: JavaScript Parsing Strategy
2.1 The Hybrid Engine: AST + Regex Fallback
Primary: AST (Abstract Syntax Tree): Parse the JS into an AST to understand the code structure (variable assignments, function calls).

Fallback: Regex Heuristics: AST parsers will crash on malformed JS or heavily obfuscated code. Always maintain a regex-based fallback to scrape strings that look like URLs or paths (/\/api\/[a-zA-Z0-9_\-\/]+/g).

2.2 Pattern Recognition Rules (AST Focus)
Template Literals: Extract the static parts of template strings (e.g., `${baseURL}/api/users/${id}` -> /api/users/).

String Concatenation: Identify expressions like base + '/api/data' and extract /api/data.

Network Call Context: Target specific function calls (fetch, axios, $.ajax, XMLHttpRequest).

Extract the first argument (the URL).

Extract configuration objects (HTTP method, required headers like Authorization).

2.3 Context-Aware Extraction & Secret Hunting (Enhanced)
While looking for endpoints, your tool must also look for security misconfigurations.

API Keys & Tokens: Add patterns to detect hardcoded AWS keys, Firebase configs, JWTs, or generic API tokens (often stored next to endpoint definitions).

Method & Header Mapping: Map discovered Content-Type expectations (e.g., does it require application/json or multipart/form-data?).

Phase 3: Advanced Extraction Techniques
3.1 Webpack & Framework Decompilation
Chunk Reassembly: Map Webpack chunk definitions to discover lazy-loaded modules.

Framework Routing: Look for frontend routing configurations (e.g., React Router <Route path="/api/...">, Vue Router configurations) which often mirror backend structures.

3.2 Variable Graph Resolution
Build a dependency graph to resolve variables within the same file.

const V1 = '/v1'; const USERS = V1 + '/users'; -> Resolves to /v1/users.

Runtime Config Limitation (Note): Acknowledge that if baseURL is fetched at runtime from an /env.json file, static AST cannot resolve it. Flag partial endpoints (e.g., [DYNAMIC_BASE]/api/auth) for active fuzzer completion later.

Phase 4: Normalization and Deduplication
4.1 Path Normalization
Strip trailing slashes, resolve ../ traversals, and remove duplicate adjacent slashes (e.g., /api/v1//users -> /api/v1/users).

4.2 Endpoint Classification & Confidence Scoring
Tag each finding to tell the active scanner how to treat it.

STATIC: /api/users/list (Confidence: High)

DYNAMIC: /api/users/{var}/profile (Confidence: High)

FRAGMENT: /users/profile (Missing base URL, Confidence: Medium)

Scoring Logic: Direct arguments to a fetch call get 0.99. A random string that happens to start with /api/ found in a comment gets 0.40.

Phase 5: Output & Active Scanner Handoff
5.1 Structured Payload
Generate a JSON payload ready for your existing WebAPI tools.

JSON
{
  "source_asset": "https://example.com/static/js/main.123.js",
  "endpoints": [
    {
      "path": "/api/auth/refresh",
      "method": "POST",
      "confidence": 0.95,
      "headers": {
        "Content-Type": "application/json",
        "Authorization": "Bearer {token}"
      },
      "body_schema": ["refresh_token"],
      "is_partial": false
    }
  ],
  "discovered_secrets": [],
  "base_urls": ["https://api.target.com"]
}
5.2 Active Discovery Integration
Feed endpoints directly into your fuzzer.

Use discovered parameters (refresh_token) to build custom wordlists for this specific target.

Phase 6: Performance & Safety Optimizations
6.1 Defensive Guardrails
Size Limits: Drop files larger than 5MB to prevent the AST parser from freezing.

Tarpit Protection: Implement strict timeouts (e.g., 10 seconds per file) for fetching and parsing.

6.2 Processing
Fetch and parse files concurrently. If parsing in a single-threaded environment (like Node.js), use worker threads for the AST parsing to avoid blocking the event loop.

Cache hashes of JS files. If a site uses standard React or jQuery libraries, hash them and skip AST parsing for known non-target libraries.

Phase 7: Edge Cases & Considerations
7.1 Obfuscation & Packing
Detect common packers (like Webpack, Browserify, or JSFuck).

Run a quick de-minification pass (e.g., Prettier/Beautifier logic) before applying regex, as regex fails on code crammed onto a single line.

7.2 False Positive Mitigation
Maintain a blacklist of common third-party domains (Google Analytics, Sentry, Stripe, Facebook SDK). Do not output endpoints destined for these domains unless explicitly instructed.

Filter out standard XML/SVG namespace URLs often found in frontend bundles.
