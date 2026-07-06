-- ============================================================
-- Migration : ajout de content_hash pour la détection de doublon
-- À exécuter manuellement sur une base Step 3 existante
-- (init.sql ne s'exécute qu'au premier démarrage d'un volume vide)
-- ============================================================

ALTER TABLE documents
    ADD COLUMN IF NOT EXISTS content_hash VARCHAR(64) NOT NULL DEFAULT '';

CREATE INDEX IF NOT EXISTS ix_documents_user_filename_hash ON documents (user_id, file_name, content_hash);
