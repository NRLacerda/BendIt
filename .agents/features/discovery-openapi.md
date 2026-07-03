# OpenAPI Specification Discovery

Discovers API routes by probing standard documentation paths and parsing OpenAPI metadata.

## Inputs

- **Base URL** (`string`): The root address of the target API.
- **Use Spec Discovery** (`boolean`): Toggle indicator determining if OpenAPI probes should run.
- **Max Body Bytes** (`int64`): Maximum body size limit when fetching specs (default 1MB).
- **Timeout Seconds** (`int`): Connection timeout configuration.

## Processing

Implementation is isolated under `backend/Discovery/DiscoveryOpenApi.cs`; `DiscoveryOrchestrator` invokes this step after WebPage JavaScript discovery when `isWebPage` is true, before API-list discovery, and delegates extracted route verification to the shared verification step. Parallelism is limited to that verification step.

1. **Spec Probing**:
   - Loops through a built-in routes list (native spec paths, e.g. `/openapi.json`, `/swagger.json`, `/v2/api-docs`, `/api-docs`).
   - Dispatches a standard `GET` request for each target path.
   - If the request completes successfully (HTTP 2xx) and the payload contains OpenAPI keywords (`openapi`, `swagger`, `paths:`), parses the payload content.
2. **Spec Parsing**:
   - Loads the JSON specification document using `System.Text.Json`.
   - Resolves the API server base path using the spec server configuration (falls back to the base URL if undefined).
3. **Endpoint Extraction**:
   - Loops through the defined paths and extraction methods.
   - Normalizes path variables (e.g. replaces `{id}` placeholders with dummy values like `123` to construct query-safe endpoints).
4. **Normalisation & Registration**:
   - Normalizes the generated candidate URLs (strips query parameters, lowercases hostnames, collapses `/users/123` to template `/users/{id}`).
   - Registers each route with a default confidence rating of 100 and source category `"openapi"`.

## Outputs

- **Candidate Endpoints** (`[]Endpoint`): An array of endpoints discovered from the OpenAPI spec, marked with a confidence of 100, including their path templates, methods, and base host.
