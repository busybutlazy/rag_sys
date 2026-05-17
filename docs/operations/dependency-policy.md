# Dependency Update Policy

## Cadence

- Review Docker base images monthly and on relevant security advisories.
- Review Python, npm, and .NET dependencies monthly; fast-track critical CVEs.

## Python

- Keep `pyproject.toml` constrained and produce reproducible lock artifacts before production deployment.
- Run `ruff` and Python tests after upgrades.

## Frontend

- Run `npm audit` and `npm outdated` during the monthly review.
- Commit `package-lock.json` changes together with dependency upgrades.

## .NET

- Run `dotnet list package --vulnerable` and `dotnet list package --outdated`.
- Run the full BE test suite after package upgrades.

## Docker

- Pin major runtime families intentionally.
- Rebuild and smoke-test compose after base-image refreshes.
