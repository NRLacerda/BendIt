# Parameter Pollution Validator

## Purpose

The parameter pollution validator checks whether an API safely handles duplicated request parameters. It sends repeated query keys and, for body-capable methods, repeated form and JSON keys. The goal is to find APIs that silently collapse ambiguous values into a dangerous authorization or object-selection decision.

## Probes

- `duplicateQuery`: sends repeated query parameters such as `id=1&id=2&role=user&role=admin`.
- `duplicateFormBody`: sends repeated `application/x-www-form-urlencoded` body keys.
- `duplicateJsonKeys`: sends JSON with duplicate object keys.

The test prefers discovered endpoint query parameters. If discovery did not find query parameters, it uses controlled fallback keys: `id` and `role`.

## Finding Logic

The validator treats explicit rejection with client errors as safe evidence. It raises risk when a successful response indicates that duplicated values were accepted, especially when the response shows last-value-wins behavior for:

- `role=admin`
- `isAdmin=true`
- alternate `id`, `tenantId`, `ownerId`, or similar object identifiers
- response markers such as `winner`, `lastValue`, `polluted`, or duplicated-value reflection

Server errors caused by duplicate parameters are also suspicious because they show parser or binding fragility.

## OWASP Mapping

- Defaults to `API10: Unsafe Consumption of APIs` for ambiguous duplicate-parameter handling.
- Escalates to `API3: Broken Object Property Level Authorization` when privileged or object-control values are accepted.

## Safety

The probes use small bounded payloads and only test duplicated primitive values. They do not attempt destructive state changes beyond the configured endpoint method itself.
