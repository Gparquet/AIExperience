-- Migration : ajout des timestamps temporels sur les chunks vidéo
-- Colonnes nullable — aucun chunk existant n'est impacté.
ALTER TABLE document_chunks
    ADD COLUMN IF NOT EXISTS start_time_seconds double precision NULL,
    ADD COLUMN IF NOT EXISTS end_time_seconds double precision NULL;
