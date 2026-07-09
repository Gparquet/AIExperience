# ADR-005 — Transcription vidéo/audio 100 % locale (Whisper.net + FFmpeg) plutôt qu'une API cloud

**Date :** 2026-07-05  
**Statut :** Accepté  
**Auteur :** Geoffrey

---

## Contexte

Step 3 introduit une capacité majeure : **ingérer des vidéos et fichiers audio** dans le RAG.
Cela suppose de transcrire l'audio en texte (avec des timestamps, cf.
[ADR-006](006-chunking-temporel-video.md)) avant chunking et embedding.

Deux familles de solutions existent pour la transcription (speech-to-text) :

- **Cloud** : OpenAI Whisper API, Azure AI Speech, Google Speech-to-Text… Qualité élevée,
  aucune ressource locale, mais envoi des fichiers à un tiers, coût à l'usage, dépendance réseau.
- **Locale** : exécuter un modèle de transcription sur la machine hôte. Aucun envoi externe,
  gratuit à l'usage, mais consomme CPU/GPU et exige une installation.

Le projet est déjà résolument orienté **exécution locale** : le LLM et les embeddings tournent
via LM Studio / Ollama en local. La transcription doit rester cohérente avec cette philosophie.

---

## Décision

Transcrire **entièrement en local**, sans aucun appel cloud, via :

- **FFmpeg** (through `FFMpegCore`) pour extraire la piste audio d'une vidéo et la normaliser.
- **Whisper.net** (wrapper C# de `whisper.cpp`) pour la transcription proprement dite.

### Pourquoi local plutôt que cloud ?

| Critère | Local (Whisper.net + FFmpeg) | Cloud (OpenAI/Azure Speech) |
|---------|------------------------------|------------------------------|
| Confidentialité | ✅ Aucun fichier ne quitte la machine | ❌ Upload de l'audio à un tiers |
| Coût à l'usage | ✅ Nul | ❌ Facturé à la minute |
| Dépendance réseau | ✅ Fonctionne hors-ligne | ❌ Requiert une connexion stable |
| Cohérence projet | ✅ Aligné avec LLM/embeddings locaux | ❌ Introduit une dépendance cloud isolée |
| Qualité | Bonne (modèle `small`/`medium`) | Excellente |
| Prérequis machine | ❌ CPU/RAM + installation manuelle | ✅ Aucun |

La **confidentialité**, l'**absence de coût** et la **cohérence** avec le reste de la stack
locale l'emportent, au prix d'un temps de traitement CPU et de prérequis d'installation.

---

## Architecture de la solution

### Pipeline en deux étages

```
Fichier vidéo/audio
   │
   ├─ 1. FFmpegVideoProcessorService.ExtractAudioAsync()
   │       → extrait/convertit en WAV 16 kHz mono (format attendu par Whisper)
   │
   └─ 2. WhisperTranscriptionService.TranscribeAsync()
           → TranscriptionResult { FullText, Segments[], Duration, Language }
```

### Couche Domain — abstractions sans dépendance technique

Le Domain ne connaît ni FFmpeg ni Whisper, seulement des contrats :

```csharp
// Extraction audio
Task<string> ExtractAudioAsync(string videoPath, string outputAudioPath, CancellationToken ct);
bool IsSupported(string filePath);

// Transcription
Task<TranscriptionResult> TranscribeAsync(string audioPath, string language, CancellationToken ct);
```

Les implémentations vivent en Infrastructure, injectées via `AddVideoTranscription()`.

### `WhisperTranscriptionService` — singleton à init paresseuse thread-safe

Le modèle `ggml-*.bin` (plusieurs centaines de Mo) est **coûteux à charger**. Le service est
donc un **singleton** qui charge le modèle **une seule fois**, protégé par un `SemaphoreSlim` +
`Lazy<T>` pour être sûr en concurrence. Configuration via `WhisperOptions` :
`ModelPath` (chemin du `.bin`) et `Threads` (parallélisme CPU).

### `FFmpegVideoProcessorService` — normalisation audio

Convertit toute entrée supportée (`.mp4`, `.mkv`, `.webm`, `.avi`, `.mov`, `.wav`, `.mp3`,
`.m4a`, `.ogg`, `.flac`) en **WAV 16 kHz mono**, format d'entrée requis par whisper.cpp.
Chemin du binaire configuré via `FFmpeg:BinaryPath`.

### Prérequis d'installation manuelle (assumés)

1. **Modèle Whisper** : téléchargé manuellement depuis Hugging Face
   (`ggerganov/whisper.cpp`), ex. `ggml-small.bin`, chemin renseigné dans `appsettings.json`.
2. **FFmpeg** : installé sur la machine hôte, chemin renseigné dans `appsettings.json`.

Ces prérequis sont le prix explicite du choix local et sont documentés dans le CLAUDE.md Back.

---

## Conséquences

### Positives

- **Zéro fuite de données** : les fichiers, souvent sensibles, ne quittent jamais la machine.
- **Zéro coût** à l'usage et **fonctionnement hors-ligne**.
- **Cohérence** totale avec la stack IA locale (LLM + embeddings).
- Traitement chargé une fois (modèle en mémoire) puis réutilisé pour tous les jobs.

### Négatives / points d'attention

- **Temps CPU** : ~10–15 s par minute d'audio avec `ggml-small` sur CPU moderne. C'est
  précisément ce qui a motivé l'ingestion asynchrone ([ADR-002](002-ingestion-asynchrone-outbox.md)).
- **Prérequis d'installation** (modèle `.bin` + FFmpeg) : friction de mise en route, à
  documenter soigneusement.
- **Empreinte mémoire** du modèle chargé en singleton (plusieurs centaines de Mo à ~1,5 Go
  selon le modèle).
- **Réentrance non garantie** de Whisper/FFmpeg → traitement séquentiel imposé côté worker.
- Qualité en retrait par rapport aux meilleures API cloud sur audio difficile (bruit, accents).

---

## Alternatives rejetées

| Alternative | Raison du rejet |
|-------------|-----------------|
| OpenAI Whisper API (cloud) | Envoi des fichiers à un tiers, coût à la minute, dépendance réseau — contraire à la philosophie locale. |
| Azure AI Speech / Google STT | Même problème de confidentialité/coût ; verrouillage à un fournisseur. |
| Vosk / autres moteurs locaux | Qualité et écosystème .NET inférieurs à Whisper.net ; timestamps segmentés moins pratiques. |
| Whisper via appel Python externe | Introduit une dépendance à un runtime Python et à l'IPC, alors que Whisper.net s'intègre nativement en .NET. |
