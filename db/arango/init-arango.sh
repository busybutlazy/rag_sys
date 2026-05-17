#!/bin/sh
# Idempotent ArangoDB initialization via REST API.
# Runs as the arango-init service AFTER arangodb is healthy.
# Handles user, database, collections, and search view setup.
# Vector indexes are created by rag-server only after embeddings exist because
# ArangoDB cannot train a vector index on an empty collection.
set -e

ARANGO_URL="http://arangodb:8529"
ROOT_AUTH="root:${ARANGO_ROOT_PASSWORD}"
USER_AUTH="${ARANGO_USER}:${ARANGO_PASSWORD}"
EMBEDDING_DIMENSIONS="${EMBEDDING_DIMENSIONS:-1536}"

check() {
    curl -s -o /dev/null -w "%{http_code}" "$@"
}

# ── 1. Database ────────────────────────────────────────────────
echo "[arango-init] Creating database '${ARANGO_DB}'..."
STATUS=$(check -u "${ROOT_AUTH}" \
    -X POST -H "Content-Type: application/json" \
    -d "{\"name\":\"${ARANGO_DB}\"}" \
    "${ARANGO_URL}/_api/database")
if [ "$STATUS" != "201" ] && [ "$STATUS" != "409" ]; then
    echo "[arango-init] ERROR: database create returned HTTP $STATUS" >&2; exit 1
fi
echo "[arango-init] Database: $STATUS"

# ── 2. User ────────────────────────────────────────────────────
echo "[arango-init] Creating user '${ARANGO_USER}'..."
STATUS=$(check -u "${ROOT_AUTH}" \
    -X POST -H "Content-Type: application/json" \
    -d "{\"user\":\"${ARANGO_USER}\",\"passwd\":\"${ARANGO_PASSWORD}\",\"active\":true}" \
    "${ARANGO_URL}/_api/user")
if [ "$STATUS" != "201" ] && [ "$STATUS" != "409" ]; then
    echo "[arango-init] ERROR: user create returned HTTP $STATUS" >&2; exit 1
fi
echo "[arango-init] User: $STATUS"

echo "[arango-init] Syncing user password..."
curl -sf -u "${ROOT_AUTH}" \
    -X PATCH -H "Content-Type: application/json" \
    -d "{\"passwd\":\"${ARANGO_PASSWORD}\",\"active\":true}" \
    "${ARANGO_URL}/_api/user/${ARANGO_USER}" > /dev/null

echo "[arango-init] Granting rw access to '${ARANGO_DB}'..."
curl -sf -u "${ROOT_AUTH}" \
    -X PUT -H "Content-Type: application/json" \
    -d "{\"grant\":\"rw\"}" \
    "${ARANGO_URL}/_api/user/${ARANGO_USER}/database/${ARANGO_DB}" > /dev/null

# ── 3. Collections ─────────────────────────────────────────────
echo "[arango-init] Creating collections..."
for COLLECTION in documents chunks notebooks experiments; do
    STATUS=$(check -u "${USER_AUTH}" \
        -X POST -H "Content-Type: application/json" \
        -d "{\"name\":\"${COLLECTION}\"}" \
        "${ARANGO_URL}/_db/${ARANGO_DB}/_api/collection")
    if [ "$STATUS" != "200" ] && [ "$STATUS" != "201" ] && [ "$STATUS" != "409" ]; then
        echo "[arango-init] ERROR: collection '${COLLECTION}' returned HTTP $STATUS" >&2; exit 1
    fi
    echo "[arango-init] Collection ${COLLECTION}: $STATUS"
done

# ── 4. ArangoSearch view ───────────────────────────────────────
echo "[arango-init] Creating search view..."
VIEW_BODY="{\"name\":\"chunks_view\",\"type\":\"arangosearch\",\"links\":{\"chunks\":{\"fields\":{\"text\":{\"analyzers\":[\"text_en\"]},\"notebook_id\":{\"analyzers\":[\"identity\"]},\"user_id\":{\"analyzers\":[\"identity\"]},\"source_id\":{\"analyzers\":[\"identity\"]},\"chunk_index\":{}}}}}"
STATUS=$(check -u "${USER_AUTH}" \
    -X POST -H "Content-Type: application/json" \
    -d "${VIEW_BODY}" \
    "${ARANGO_URL}/_db/${ARANGO_DB}/_api/view")
if [ "$STATUS" != "200" ] && [ "$STATUS" != "201" ] && [ "$STATUS" != "409" ]; then
    echo "[arango-init] ERROR: view create returned HTTP $STATUS" >&2; exit 1
fi
echo "[arango-init] Search view: $STATUS"

echo "[arango-init] Done."
