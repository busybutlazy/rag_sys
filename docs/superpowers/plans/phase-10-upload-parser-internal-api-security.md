# Phase 10 - Upload, Parser, and Internal API Security

## Goal

Make upload and internal-service boundaries fail closed: trust observed file content over client claims, bound parser work, and reduce the blast radius of internal credentials.

## Scope

- Detect uploaded file types server-side and persist both the client-provided and detected content types.
- Cross-check extension, claimed MIME type, and detected MIME type before accepting a source.
- Add upload quotas and reject empty files before persistence.
- Add parser limits and bounded extraction time in the RAG service.
- Split internal credentials by trust boundary where practical:
  - `RAG_INTERNAL_SECRET` for callers reaching the RAG service.
  - `AI_INTERNAL_SECRET` for BE↔AI and AI→BE internal calls.
- Enforce minimum secret length in every service while keeping `INTERNAL_SECRET` as a compatibility fallback during local migration.
- Add baseline browser response headers at nginx.
- Add focused tests around spoofed uploads, unsupported uploads, quota enforcement, traversal filenames, and internal secret rejection.

## Implementation Steps

1. Extend source persistence:
   - add `OriginalContentType` and `DetectedMimeType`
   - keep `MimeType` as the normalized detected type used by ingestion
2. Harden BE upload flow:
   - inspect magic bytes / container structure
   - validate extension + claimed MIME + detected MIME
   - enforce per-user storage quota and per-notebook source cap
   - sanitize filenames and keep rejecting empty files
3. Bound RAG parsing:
   - max PDF pages
   - max extracted characters
   - max JSON bytes and nesting depth
   - max DOCX paragraphs and text length
   - timeout extraction work
4. Harden internal APIs:
   - minimum 32-character internal secrets everywhere
   - introduce separate RAG and AI internal secrets with migration fallback
   - document a rotation procedure and the future path to JWT/mTLS
5. Add frontend/nginx security headers.
6. Verify with BE tests, Python compile checks, frontend build, and Docker builds where practical.

## Non-goals

- No multi-tenant billing or quota dashboard in this phase.
- No external queue or sandboxed parser worker process yet.
- No service JWT or mTLS rollout yet; this phase prepares the seam for it.

## Risks

- Strict MIME cross-checking can reject unusual but valid files; the allowlist should prefer clear failures over silent ambiguity.
- Parser limits trade completeness for safety; operators may need to tune them for larger corpora later.
- Secret splitting introduces deployment drift risk unless environment templates and docs move in lockstep.
