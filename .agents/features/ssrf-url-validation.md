# SSRF-Safe URL Field Validation

## Purpose

The SSRF-safe URL field validator checks whether body-capable API endpoints safely reject user-controlled URL fields. It looks for unsafe server-side URL consumption without targeting internal services, localhost, metadata endpoints, or attacker-controlled callback infrastructure.

## Inputs

- **Discovered Endpoint** (`Endpoint`): A `POST`, `PUT`, or `PATCH` endpoint selected by the test battery.
- **Project Config** (`Project`): Base request headers and masked authentication context.
- **Bend Type** (`ssrfUrlValidation`): Default-enabled robustness test selected through `TestRunRequest.bendTypes`.

## Processing

1. **Safe URL Probes**:
   - Sends small JSON payloads with URL-looking fields: `url`, `targetUrl`, `callbackUrl`, and `webhookUrl`.
   - Uses only documentation/reserved HTTP(S) targets:
     - `http://example.invalid/bendit-ssrf-probe`
     - `https://example.invalid/bendit-ssrf-probe`
     - `http://example.test/bendit-ssrf-probe`
     - `https://example.test/bendit-ssrf-probe`
2. **Response Evaluation**:
   - Treats `4xx` responses as safe rejection evidence.
   - Also treats explicit rejection text as safe, including `invalid url`, `invalid uri`, `not allowed`, `disallowed`, `blocked`, `forbidden`, `unsupported`, `validation`, or `error`.
   - Raises risk when a URL probe is accepted with `2xx` or `3xx` without safe rejection text.
   - Raises higher risk for server errors, request failures, or fetch-related terms such as `dns`, `resolve`, `enotfound`, `connection refused`, `timeout`, `fetch failed`, or `download failed`.
3. **Representative Result**:
   - Aggregates all probe attempts into one result per endpoint.
   - Uses the highest-risk observation as the representative request, response, and evidence.

## Outputs

- **Test Result** (`TestResult`): One aggregated result for the endpoint.
- **Mutation Metadata**: Stores `type=ssrfUrlValidation`, representative field, representative probe URL, `safeReservedHostsOnly=true`, per-field statuses, and matched signals.
- **Evidence**: States whether the endpoint rejected/safely handled the probes or showed potential unsafe server-side URL consumption.
- **OWASP Mapping**: Maps to `API10: Unsafe Consumption of APIs`.

## Safety

- Runs only for methods that support request bodies.
- Uses documentation/reserved domains only.
- Does not probe localhost, private networks, link-local addresses, cloud metadata IPs, non-HTTP schemes, or user-provided callback hosts.
- Does not claim confirmed SSRF; findings are described as potential unsafe server-side URL consumption.
