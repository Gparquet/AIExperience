# Architecture Decision Records — AIExperience Step 3

Ce dossier recense les **décisions d'architecture** structurantes de Step 3, chacune consignée
dans un ADR (Architecture Decision Record) suivant un format commun :
**Contexte → Décision → Architecture → Conséquences → Alternatives rejetées**.

Un ADR fige le *pourquoi* d'un choix à un instant donné. Il ne se modifie pas : si une décision
évolue, on rédige un nouvel ADR qui remplace ou complète l'ancien (statut « Remplacé par… »).

## Index

| N° | Décision | Statut |
|----|----------|--------|
| [001](001-streaming-rag-reponse.md) | Streaming de la réponse RAG via Server-Sent Events (SSE) | Accepté |
| [002](002-ingestion-asynchrone-outbox.md) | Ingestion & transcription asynchrones via pattern Outbox + BackgroundService | Accepté |
| [003](003-notification-signalr.md) | Notification de fin d'ingestion en temps réel via SignalR + repli polling | Accepté |
| [004](004-upload-progression-xhr.md) | Suivi de progression d'upload via XMLHttpRequest plutôt que fetch | Accepté |
| [005](005-transcription-video-locale-whisper.md) | Transcription vidéo/audio 100 % locale (Whisper.net + FFmpeg) | Accepté |
| [006](006-chunking-temporel-video.md) | Chunking temporel des segments vidéo + propagation des timestamps | Accepté |
| [007](007-detection-doublons-upload.md) | Détection de doublons à l'upload (hash client + garde serveur 409) | Accepté |
| [008](008-workfilestore-fichiers-de-travail.md) | Fichiers de travail durables (WorkFileStore) survivant à la requête | Accepté |
| [009](009-trois-modes-restitution-rag.md) | Trois modes de restitution (Full-text / LLM direct / RAG complet) | Accepté |
| [010](010-schema-postgres-scripts-sql.md) | Schéma PostgreSQL par scripts SQL manuels (pas de migrations EF Core) | Accepté (réserves) |
| [011](011-extraction-texte-multi-format-composite.md) | Extraction de texte multi-format via extracteur composite | Accepté |
| [012](012-cqrs-maison-composition.md) | Remplacement de MediatR par un CQRS maison basé sur la composition | Accepté |

## Fils conducteurs

Plusieurs ADR forment des chaînes cohérentes qu'il est utile de lire ensemble :

- **Upload asynchrone de bout en bout :** [002](002-ingestion-asynchrone-outbox.md) (traitement
  différé) → [008](008-workfilestore-fichiers-de-travail.md) (le fichier survit à la requête) →
  [003](003-notification-signalr.md) (le front est notifié de la fin) →
  [004](004-upload-progression-xhr.md) (progression du transfert) →
  [007](007-detection-doublons-upload.md) (garde anti-doublon).
- **Pipeline vidéo :** [005](005-transcription-video-locale-whisper.md) (transcription locale) →
  [006](006-chunking-temporel-video.md) (chunking temporel + timestamps dans les citations).
- **Temps réel — deux besoins, deux outils :** [001](001-streaming-rag-reponse.md) choisit SSE
  pour le streaming attaché à une requête, [003](003-notification-signalr.md) choisit SignalR
  pour les notifications serveur→clients hors requête. Voir la comparaison dans l'ADR-003.
