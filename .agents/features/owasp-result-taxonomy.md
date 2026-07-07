# OWASP Result Taxonomy

Maps every audit result to an OWASP API Security Top 10 category and a short remediation recommendation so BendIt behaves like an OWASP-oriented validator instead of only a raw test runner.

## Inputs

- **Test Result Context** (`TestResult`): Bend type, endpoint, response status, risk score, evidence, and auth context.
- **OWASP API Category** (`string`): Stable category label such as `API1: Broken Object Level Authorization`.
- **Recommendation** (`string`): Short remediation guidance suitable for the result detail view and exported JSON.

## Processing

1. **Category Mapping**:
   - Maps each bend type to one OWASP API Security Top 10 category.
   - Keeps the mapping deterministic and local to test result creation.
2. **Recommendation Generation**:
   - Adds concise guidance based on the bend type and observed risk.
   - Keeps recommendations generic enough for most WebAPIs.
3. **Result Serialization**:
   - Persists `owaspCategory` and `recommendation` in every `TestResult`.
   - Exposes the fields to the frontend result detail view and JSON downloads.

## Outputs

- **OWASP-Mapped Results**: Result artifacts include `owaspCategory` and `recommendation` fields for dashboard grouping, export, and future report generation.
