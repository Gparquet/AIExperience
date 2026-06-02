-- ============================================================
-- RagDocumentChat — Script de création de la base de données
-- PostgreSQL 17 + pgvector
-- Compatible avec les configurations EF Core du projet
-- ============================================================

-- Extension pgvector (obligatoire)
CREATE EXTENSION IF NOT EXISTS vector;

-- ============================================================
-- TABLE : documents
-- ============================================================
CREATE TABLE IF NOT EXISTS documents (
    Id                   UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    file_name            VARCHAR(255) NOT NULL,
    content_type         VARCHAR(100) NOT NULL,
    file_size_bytes      BIGINT       NOT NULL DEFAULT 0,
    user_id              VARCHAR(255) NOT NULL,
    status               VARCHAR(50)  NOT NULL DEFAULT 'Pending',
    chunking_strategy    VARCHAR(50)  NOT NULL DEFAULT 'Recursive',
    file_reference       VARCHAR(500),
    error_message        VARCHAR(2000),
    -- Owned type DocumentMetadata (aplati dans la même table)
    metadata_title       VARCHAR(500),
    metadata_author      VARCHAR(255),
    metadata_page_count  INTEGER      NOT NULL DEFAULT 0,
    metadata_language    VARCHAR(10)  NOT NULL DEFAULT 'fr',
    metadata_tags        JSONB        NOT NULL DEFAULT '[]',
    created_at           TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at           TIMESTAMPTZ  NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_documents_user_id   ON documents (user_id);
CREATE INDEX IF NOT EXISTS ix_documents_status    ON documents (status);
CREATE INDEX IF NOT EXISTS ix_documents_created_at ON documents (created_at);

-- ============================================================
-- TABLE : document_chunks
-- Vecteur d'embedding : 3072 dims (AzureOpenAI/GitHubModels)
--                    ou 768 dims  (Ollama nomic-embed-text)
-- ⚠️  Adapter vector(3072) selon votre provider AI
-- ============================================================
CREATE TABLE IF NOT EXISTS document_chunks (
    Id                   UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    document_id          UUID        NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
    content              TEXT        NOT NULL,
    chunk_index          INTEGER     NOT NULL DEFAULT 0,
    page_number          INTEGER,
    section_title        VARCHAR(500),
    embedding_dimensions INTEGER NOT NULL DEFAULT 768,
	embedding            vector(768),         
    created_at           TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_document_chunks_document_id  ON document_chunks (document_id);
CREATE INDEX IF NOT EXISTS ix_document_chunks_chunk_index  ON document_chunks (chunk_index);

-- HNSW fonctionne car 768 ≤ 2000 ✅
CREATE INDEX IF NOT EXISTS ix_document_chunks_embedding_hnsw
    ON document_chunks
    USING hnsw (embedding vector_cosine_ops)
    WITH (m = 16, ef_construction = 64);

-- ============================================================
-- TABLE : conversation_sessions
-- ============================================================
CREATE TABLE IF NOT EXISTS conversation_sessions (
    Id          UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id     VARCHAR(255) NOT NULL,
    title       VARCHAR(500) NOT NULL DEFAULT '',
    created_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at  TIMESTAMPTZ  NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_conversation_sessions_user_id    ON conversation_sessions (user_id);
CREATE INDEX IF NOT EXISTS ix_conversation_sessions_updated_at ON conversation_sessions (updated_at);

-- ============================================================
-- TABLE : chat_messages
-- ============================================================
CREATE TABLE IF NOT EXISTS chat_messages (
    Id           UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id   UUID        NOT NULL REFERENCES conversation_sessions(id) ON DELETE CASCADE,
    role         VARCHAR(20) NOT NULL,           -- 'User' | 'Assistant' | 'System'
    content      TEXT        NOT NULL,
    tokens_used  INTEGER     NOT NULL DEFAULT 0,
    strategy_used VARCHAR(20),                   -- 'Direct' | 'HyDE' | 'Fusion' | 'Adaptive'
    duration_ms  BIGINT      NOT NULL DEFAULT 0,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_chat_messages_session_id ON chat_messages (session_id);
CREATE INDEX IF NOT EXISTS ix_chat_messages_created_at ON chat_messages (created_at);

-- ============================================================
-- TABLE : citations
-- ============================================================
CREATE TABLE IF NOT EXISTS citations (
    Id            UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    message_id    UUID        NOT NULL REFERENCES chat_messages(id) ON DELETE CASCADE,
    document_id   UUID        NOT NULL,
    document_name VARCHAR(255) NOT NULL,
    page_number   INTEGER,
    excerpt       TEXT        NOT NULL,
    score         DOUBLE PRECISION NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS ix_citations_message_id  ON citations (message_id);
CREATE INDEX IF NOT EXISTS ix_citations_document_id ON citations (document_id);

-- ============================================================
-- TABLE : outbox_messages  (Outbox Pattern)
-- ============================================================
CREATE TABLE IF NOT EXISTS outbox_messages (
    Id           UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    event_type   VARCHAR(500) NOT NULL,
    payload      JSONB        NOT NULL,
    created_at   TIMESTAMPTZ  NOT NULL DEFAULT now(),
    processed_at TIMESTAMPTZ,
    error        VARCHAR(2000),
    retry_count  INTEGER      NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS ix_outbox_messages_processed_at ON outbox_messages (processed_at);
CREATE INDEX IF NOT EXISTS ix_outbox_messages_created_at   ON outbox_messages (created_at);