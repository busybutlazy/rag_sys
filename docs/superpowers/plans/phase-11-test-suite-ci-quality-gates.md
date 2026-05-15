# Phase 11 - Test Suite and CI Quality Gates

## Goal

Give every change a repeatable path to prove itself before merge: focused backend tests, lightweight frontend smoke checks, Python coverage for RAG logic, linters, and CI automation.

## Scope

- Expand BE coverage around ownership and validation edges.
- Add Python unit tests for chunking, request models, secret guards, and vector-store query construction.
- Add frontend smoke checks without introducing a browser harness yet.
- Add formatting/lint commands for .NET, Python, and frontend TypeScript.
- Add a GitHub Actions workflow for build/test/lint, plus an optional compose smoke job when deployment secrets are available.

## Implementation Steps

1. Extend BE tests:
   - notebook ownership
   - search query/mode validation
   - source upload validation already introduced in Phase 10 remains part of the suite
2. Add Python `unittest` coverage:
   - chunk overlap behavior
   - vector query bind vars
   - request model validation
   - internal secret boot checks
3. Add frontend smoke checks:
   - login page presence
   - protected-route redirect contract
   - login error display contract
4. Add tooling:
   - `.editorconfig`
   - `dotnet format` CI verification
   - `ruff` config and checks
   - ESLint config and script
5. Add CI workflow with parallel jobs and an optional compose smoke path.

## Non-goals

- No full browser automation suite yet.
- No broad integration-test fixture system yet.
- No test-data factory refactor yet; that belongs with later maintainability work.
