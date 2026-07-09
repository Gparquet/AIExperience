-- ============================================================
-- Migration : transcription persistée pour consultation après coup
-- (l'ingestion vidéo devient asynchrone, la transcription n'est plus
-- renvoyée en synchrone dans la réponse d'upload)
-- À exécuter manuellement sur une base Step 3 existante
-- (init.sql ne s'exécute qu'au premier démarrage d'un volume vide)
--
-- La table outbox_messages, elle, existe déjà dans init.sql — rien à
-- ajouter côté SQL pour la file d'attente elle-même.
-- ============================================================

ALTER TABLE documents
    ADD COLUMN IF NOT EXISTS raw_transcription     TEXT,
    ADD COLUMN IF NOT EXISTS cleaned_transcription TEXT,
    ADD COLUMN IF NOT EXISTS index_in_rag                 BOOLEAN NOT NULL DEFAULT true,
    ADD COLUMN IF NOT EXISTS clean_transcription_with_llm BOOLEAN NOT NULL DEFAULT false;
