-- Ajoute la colonne d'avancement fin d'ingestion aux bases Step3 déjà créées.
-- Idempotent : ne fait rien si la colonne existe déjà.
ALTER TABLE documents
    ADD COLUMN IF NOT EXISTS ingestion_progress jsonb NULL;
