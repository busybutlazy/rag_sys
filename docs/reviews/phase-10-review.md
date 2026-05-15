# Phase 10 Review - Upload, Parser, and Internal API Security

## Findings

No patch-required findings.

## Review Notes

- Upload trust now comes from inspected content rather than only client-supplied MIME metadata.
- Text-like uploads are accepted only after UTF-8 validation plus extension/content-type cross-checking; ambiguous binaries fail closed.
- Parser work is bounded by size, depth, page, paragraph, character, and wall-clock limits.
- Internal credentials are now split by trust boundary where practical, while the documented compatibility fallback keeps local migration manageable.
- Baseline browser response headers are emitted by nginx.

## Residual Risk

- Text-like formats cannot be proven with the same certainty as binary signatures; the current design intentionally requires agreement between validated text content, extension, and declared MIME type.
- Raw shared secrets still remain a deployment convention. Service JWTs or mTLS should replace them before multi-host deployment.
- Parser work is bounded but still runs in-process; a future sandboxed worker would improve isolation for hostile documents.

## Verification

- `docker run --rm -v /home/jett/Documents/rag_sys/be-server:/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet test BeServer.Tests/BeServer.Tests.csproj --logger "console;verbosity=minimal"`
- `python3 -m compileall rag-server/app ai-server/app`
- `npm run build` in `frontend/`
- `docker compose build be-server ai-server rag-server frontend`
