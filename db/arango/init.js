// Phase 0 skeleton: create rag_db and seed collections.
// Collections populated with data in Phase 2+.

const ARANGO_DB = (process.env.ARANGO_DB || "rag_db");
const ARANGO_USER = (process.env.ARANGO_USER || "raguser");
const ARANGO_PASSWORD = (process.env.ARANGO_PASSWORD || "ragpass");
const users = require("@arangodb/users");

// Create database if it doesn't exist
if (!db._databases().includes(ARANGO_DB)) {
  db._createDatabase(ARANGO_DB);
  print(`Created database: ${ARANGO_DB}`);
} else {
  print(`Database already exists: ${ARANGO_DB}`);
}

// Keep initialization idempotent for existing Docker volumes.
if (!users.exists(ARANGO_USER)) {
  users.save(ARANGO_USER, ARANGO_PASSWORD, true);
  print(`Created user: ${ARANGO_USER}`);
} else {
  users.update(ARANGO_USER, ARANGO_PASSWORD, true);
  print(`Updated user: ${ARANGO_USER}`);
}
users.grantDatabase(ARANGO_USER, ARANGO_DB, "rw");
users.grantCollection(ARANGO_USER, ARANGO_DB, "*", "rw");
users.reload();

// Switch to the new database
db._useDatabase(ARANGO_DB);

// Phase 0: skeleton collections only
const collections = ["documents", "chunks", "notebooks"];
for (const name of collections) {
  if (!db._collection(name)) {
    db._create(name);
    print(`Created collection: ${name}`);
  } else {
    print(`Collection already exists: ${name}`);
  }
}
