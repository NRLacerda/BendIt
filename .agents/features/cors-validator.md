# CORS Validator

## Purpose

The CORS validator checks whether an API exposes browser-readable cross-origin access to arbitrary origins. It focuses on security misconfiguration signals that can allow attacker-controlled web pages to read API responses or send credentialed requests.

## Probes

- `actualAttackerOrigin`: sends the endpoint method with `Origin: https://evil.example`.
- `actualSecondOrigin`: sends the endpoint method with a second arbitrary origin to detect origin reflection.
- `preflightAttackerOrigin`: sends `OPTIONS` with `Origin`, `Access-Control-Request-Method`, and sensitive requested headers.

## Finding Logic

The validator treats missing CORS headers as low risk because browsers will not expose the response cross-origin without allow headers. It raises risk when responses include:

- `Access-Control-Allow-Origin: *`
- attacker-origin reflection
- `Access-Control-Allow-Credentials: true` with wildcard or attacker origins
- preflight approval for sensitive headers such as `Authorization`, `Cookie`, or `X-API-Key`
- broad preflight methods such as `PUT`, `PATCH`, or `DELETE`

## OWASP Mapping

CORS findings map to `API8: Security Misconfiguration`.

## Safety

The validator uses bounded GET-style and OPTIONS-style requests with custom headers. It does not send large payloads or destructive bodies.
