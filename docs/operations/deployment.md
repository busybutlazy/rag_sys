# Deployment Baseline

## Production compose

Use the production overlay with real secrets:

```bash
docker compose --env-file .env.prod -f docker-compose.yml -f docker-compose.prod.yml up -d --build
```

The base network keeps MySQL, ArangoDB, BE, AI, and RAG internal. Only the frontend port is published.

## Required checks

- Do not use values from `.env.template` in production.
- Set `ASPNETCORE_ENVIRONMENT=Production` through the overlay.
- Back up all three persistent data surfaces together:
  - MySQL relational state
  - ArangoDB retrieval state
  - uploaded files volume
- Use `/health` for liveness and `/ready` for dependency readiness.
- Inspect `/metrics` inside the internal network when diagnosing queue depth or request errors.
