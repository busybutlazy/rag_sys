-- Phase 0 skeleton: users table only.
-- Full schema built incrementally in Phase 1+.

CREATE TABLE IF NOT EXISTS users (
  id           CHAR(36)      NOT NULL DEFAULT (UUID()),
  username     VARCHAR(64)   NOT NULL,
  password_hash VARCHAR(255) NOT NULL,
  created_at   DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at   DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (id),
  UNIQUE KEY uq_users_username (username)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
