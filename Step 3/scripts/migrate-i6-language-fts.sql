-- Step 3/scripts/migrate-i6-language-fts.sql
-- ============================================================
-- Migration I-6 (Lot 2) : content_tsv n'est plus une colonne GENERATED.
-- À exécuter sur une base Step 3 déjà créée avec l'ancien init.sql
-- (colonne content_tsv générée avec 'french' figé).
--
-- to_tsvector(regconfig, text) est STABLE (pas IMMUTABLE) : Postgres interdit
-- les colonnes générées dont le regconfig varie par ligne. Cette migration
-- convertit content_tsv en colonne normale, peuplée par l'application.
-- ============================================================

ALTER TABLE document_chunks DROP COLUMN IF EXISTS content_tsv;
ALTER TABLE document_chunks ADD COLUMN content_tsv tsvector;

-- Backfill : les chunks déjà indexés gardent la langue historique 'french'
-- (pas de ré-détection automatique — cohérent avec la limitation déjà actée pour R-15).
UPDATE document_chunks SET content_tsv = to_tsvector('french', content);

CREATE INDEX IF NOT EXISTS ix_document_chunks_content_tsv
    ON document_chunks
    USING gin (content_tsv);
