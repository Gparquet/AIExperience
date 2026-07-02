-- ============================================================
-- Migration Lot 1 — Full-Text Search (I-17 / I-18)
-- Cible : bases PostgreSQL existantes créées avec un init.sql
--         antérieur au Lot 1 (avant l'ajout de content_tsv).
--
-- Ce script est IDEMPOTENT : il peut être rejoué sans erreur
-- sur une base déjà migrée (IF NOT EXISTS / DO $$ WHEN … $$).
--
-- Appliquer avec :
--   psql -h localhost -p 5433 -U postgres -d ragdocumentchat -f migrate-lot1-fulltext.sql
-- ============================================================

-- ── Étape 1 : Colonnes temporelles vidéo (I-18) ──────────────────────────────
-- Ajoutées dans le Lot 1 pour supporter les timestamps Whisper.
-- Si les colonnes existent déjà, les ADD COLUMN IF NOT EXISTS sont sans effet.

ALTER TABLE document_chunks
    ADD COLUMN IF NOT EXISTS start_time_seconds DOUBLE PRECISION,
    ADD COLUMN IF NOT EXISTS end_time_seconds   DOUBLE PRECISION;

-- ── Étape 2 : Colonne tsvector générée (I-17) ────────────────────────────────
-- GENERATED ALWAYS AS … STORED : calculé une fois à l'INSERT/UPDATE,
-- stocké physiquement → exploitation par l'index GIN sans coût de calcul.
--
-- PostgreSQL n'autorise pas ADD COLUMN IF NOT EXISTS sur une GENERATED COLUMN.
-- On utilise donc un bloc DO pour vérifier l'existence avant l'ajout.

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM   information_schema.columns
        WHERE  table_name  = 'document_chunks'
          AND  column_name = 'content_tsv'
    ) THEN
        -- Ajout de la colonne générée (langue 'french' fixée — I-6 non encore adressé)
        ALTER TABLE document_chunks
            ADD COLUMN content_tsv tsvector
                GENERATED ALWAYS AS (to_tsvector('french', content)) STORED;

        RAISE NOTICE 'Colonne content_tsv ajoutée à document_chunks.';
    ELSE
        RAISE NOTICE 'Colonne content_tsv déjà présente — aucune action.';
    END IF;
END;
$$;

-- ── Étape 3 : Index GIN sur content_tsv (I-17) ───────────────────────────────
-- Permet à SearchFullTextAsync d'atteindre O(log N) via @@ plainto_tsquery.
-- CREATE INDEX IF NOT EXISTS est idempotent.

CREATE INDEX IF NOT EXISTS ix_document_chunks_content_tsv
    ON document_chunks
    USING gin (content_tsv);

-- ── Vérification ──────────────────────────────────────────────────────────────
-- Requête diagnostic : affiche les colonnes clés après migration.
SELECT column_name, data_type, is_nullable, generation_expression
FROM   information_schema.columns
WHERE  table_name = 'document_chunks'
ORDER  BY ordinal_position;
