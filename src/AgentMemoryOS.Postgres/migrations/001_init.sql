-- Idempotent bootstrap for the MAF MemoryOS Postgres + pgvector backing store.
--
-- The {dim} token is replaced at bootstrap time with the configured embedding
-- dimension (default 256) so the vector column matches the active embedder.
--
-- Rollback for a greenfield deployment is a drop/recreate:
--   DROP TABLE IF EXISTS maf_memory_vectors;
--   DROP TABLE IF EXISTS maf_memory_facts;
-- (Documented only; the store never destroys data.)

CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS maf_memory_facts (
    scope_key   text        NOT NULL,
    content_key text        NOT NULL,
    content     text        NOT NULL,
    count       integer     NOT NULL,
    updated_at  timestamptz NOT NULL,
    CONSTRAINT pk_maf_memory_facts PRIMARY KEY (scope_key, content_key)
);

CREATE INDEX IF NOT EXISTS ix_maf_memory_facts_scope
    ON maf_memory_facts (scope_key);

CREATE TABLE IF NOT EXISTS maf_memory_vectors (
    scope_key text         NOT NULL,
    id        text         NOT NULL,
    content   text         NOT NULL,
    embedding vector({dim}) NOT NULL,
    CONSTRAINT pk_maf_memory_vectors PRIMARY KEY (scope_key, id)
);

CREATE INDEX IF NOT EXISTS ix_maf_memory_vectors_scope
    ON maf_memory_vectors (scope_key);
