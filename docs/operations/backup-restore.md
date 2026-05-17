# Backup and Restore Runbook

Back up MySQL, ArangoDB, and uploads as one logical set. Restoring only one surface can leave SQL metadata and retrieval payloads out of sync.

## Backup

```bash
mkdir -p backups
docker compose exec -T mysql sh -lc 'mysqldump -u root -p"$MYSQL_ROOT_PASSWORD" "$MYSQL_DATABASE"' > backups/mysql.sql
docker compose exec -T arangodb sh -lc 'arangodump --server.endpoint tcp://127.0.0.1:8529 --server.username root --server.password "$ARANGO_ROOT_PASSWORD" --server.database "$ARANGO_DB" --output-directory /tmp/arango-dump'
docker compose cp arangodb:/tmp/arango-dump backups/arango-dump
docker run --rm -v rag-sys_uploads:/data -v "$PWD/backups:/backup" alpine tar czf /backup/uploads.tgz -C /data .
```

## Restore

Stop application writers first, then restore in this order:

```bash
docker compose stop frontend be-server ai-server rag-server
cat backups/mysql.sql | docker compose exec -T mysql sh -lc 'mysql -u root -p"$MYSQL_ROOT_PASSWORD" "$MYSQL_DATABASE"'
docker compose cp backups/arango-dump arangodb:/tmp/arango-dump
docker compose exec -T arangodb sh -lc 'arangorestore --server.endpoint tcp://127.0.0.1:8529 --server.username root --server.password "$ARANGO_ROOT_PASSWORD" --server.database "$ARANGO_DB" --input-directory /tmp/arango-dump --create-database true'
docker run --rm -v rag-sys_uploads:/data -v "$PWD/backups:/backup" alpine sh -lc 'rm -rf /data/* && tar xzf /backup/uploads.tgz -C /data'
docker compose up -d
```

After restore, verify `/ready` for BE, AI, and RAG, then sample notebook search and source content retrieval.
