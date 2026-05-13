// Migration helper for future one-ArangoDB-database-per-user promotion.
//
// Run with root credentials, for example:
// arangosh --server.endpoint tcp://127.0.0.1:8529 \
//   --server.username root --server.password "$ARANGO_ROOT_PASSWORD" \
//   --javascript.execute scripts/arango_one_db_per_user.js

const users = require("@arangodb/users");

const USER_ID = process.env.USER_ID;
const TARGET_DB = process.env.TARGET_DB || (USER_ID ? `rag_user_${USER_ID.replace(/-/g, "_")}` : "");
const TARGET_USER = process.env.TARGET_USER || process.env.ARANGO_USER || "raguser";
const TARGET_PASSWORD = process.env.TARGET_PASSWORD || process.env.ARANGO_PASSWORD || "ragpass";

if (!USER_ID || !TARGET_DB) {
  throw new Error("USER_ID is required. Optional: TARGET_DB, TARGET_USER, TARGET_PASSWORD.");
}

if (!db._databases().includes(TARGET_DB)) {
  db._createDatabase(TARGET_DB);
  print(`Created database ${TARGET_DB}`);
}

if (!users.exists(TARGET_USER)) {
  users.save(TARGET_USER, TARGET_PASSWORD, true);
} else if (TARGET_PASSWORD) {
  users.update(TARGET_USER, TARGET_PASSWORD, true);
}

users.grantDatabase(TARGET_USER, TARGET_DB, "rw");
users.grantCollection(TARGET_USER, TARGET_DB, "*", "rw");
users.reload();

db._useDatabase(TARGET_DB);
for (const name of ["documents", "chunks", "notebooks", "experiments"]) {
  if (!db._collection(name)) {
    db._create(name);
    print(`Created collection ${TARGET_DB}.${name}`);
  }
}

print(`Prepared ${TARGET_DB} for user ${USER_ID}`);
