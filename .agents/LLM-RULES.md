# LLM Rules for Codebase Maintenance

Every feature, edit, or update made to the BendIt codebase must be registered and kept up-to-date in its corresponding feature specification file.

## Requirements

1. **Update on Change**: Whenever a backend behavior, API schema, input/output format, or verification logic is modified, the developer/LLM must update the matching specification file in `.agents/features/`.
2. **New Features**: Any new capability introduced (e.g. new test battery types, new discovery mechanisms) must receive a new markdown document inside `.agents/features/` detailing its inputs, processing logic, and outputs.
3. **Accuracy**: Feature documents must stay clear, technical, and accurate to represent how the current code behaves (no placeholder descriptions).
