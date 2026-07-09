# ADR-006 — Chunking temporel des segments vidéo et propagation des timestamps jusqu'aux citations

**Date :** 2026-07-05  
**Statut :** Accepté  
**Auteur :** Geoffrey

---

## Contexte

Une transcription vidéo n'est pas un texte plat : Whisper produit une **liste de segments**,
chacun porteur d'un intervalle temporel (`Start`, `End` en secondes) et d'un bout de texte.

Le chunker « classique » du RAG (`RecursiveChunker`, 800 chars / 100 overlap) découpe du texte
brut **sans notion de temps**. L'appliquer à une transcription détruirait deux propriétés
précieuses :

1. **L'ancrage temporel** : savoir qu'un passage cité correspond à `[00:03:12 → 00:03:20]` de la
   vidéo — indispensable pour, à terme, ouvrir un lecteur à la bonne position.
2. **L'intégrité des segments** : un découpage à l'aveugle couperait au milieu d'un segment
   Whisper, mélangeant deux instants et brouillant les timestamps.

Il faut donc une stratégie de chunking **spécifique aux contenus temporels**.

---

## Décision

Introduire **`ITemporalChunker`** (couche Domain) et son implémentation `TemporalChunker`
(couche Application), dédiés au découpage de segments transcrits, et **propager les timestamps**
tout au long de la chaîne : `chunk → DocumentChunk (persisté) → Citation → réponse API → front`.

---

## Architecture de la solution

### Chunking respectant les frontières temporelles

```csharp
Task<IReadOnlyList<TextChunk>> ChunkSegments(
    IReadOnlyList<TranscriptionSegment> segments,
    int maxCharsPerChunk);
```

Règles du `TemporalChunker` :

- **Accumule** les segments Whisper jusqu'à atteindre `maxCharsPerChunk` (~1400 chars cible).
- **Ne coupe jamais un segment en deux** : la frontière d'un chunk tombe toujours sur une
  frontière de segment (un segment surdimensionné est scindé à part, en dernier recours).
- **Chevauche 1 segment** entre deux chunks consécutifs pour préserver la continuité du sens.
- Renseigne, pour chaque chunk, `StartTime` (début du 1er segment) et `EndTime` (fin du dernier).

### Propagation du temps dans la persistance

L'entité `DocumentChunk` gagne deux propriétés optionnelles :

```csharp
public TimeSpan? StartTime { get; init; }   // début du segment temporel (vidéos)
public TimeSpan? EndTime { get; init; }      // fin du segment temporel (vidéos)
```

Persistées en base comme colonnes `start_time_seconds` / `end_time_seconds`
(`double precision`, nullables), via un **value converter EF Core** (`TimeSpan` ↔ `double`).
Elles sont **nulles** pour les documents non-vidéo, ce qui rend le modèle uniforme : un chunk
issu d'un PDF et un chunk issu d'une vidéo cohabitent dans la même table.

### Propagation jusqu'aux citations et à l'API

Lors de la génération des citations, les timestamps du chunk source sont recopiés dans la
`Citation` (propriétés `[NotMapped]`, non persistées — dérivées du chunk), puis exposés dans le
DTO :

```csharp
public double? StartTimeSeconds { get; init; }  // null si document non-vidéo
public double? EndTimeSeconds { get; init; }
```

Le front affiche alors `[HH:MM:SS]` sur les citations issues d'une vidéo (le lien vers un lecteur
positionné restant un TODO UX, la donnée étant déjà disponible).

### Format du contenu chunké

Chaque chunk conserve dans son texte les repères temporels lisibles, ex.
`[HH:MM:SS → HH:MM:SS] texte…`, ce qui aide aussi le LLM à situer les passages.

---

## Conséquences

### Positives

- Les citations vidéo sont **ancrées dans le temps** — base d'une navigation « cliquer pour
  aller à l'instant » (fonctionnalité future).
- **Aucun segment coupé** : les timestamps restent exacts et non mélangés.
- Modèle de données **uniforme** : PDF et vidéo partagent `document_chunks`, les colonnes
  temporelles restant simplement nulles hors vidéo.
- Le chevauchement d'un segment préserve la cohérence sémantique aux jointures de chunks.

### Négatives / points d'attention

- **Deux stratégies de chunking** à maintenir (`RecursiveChunker` pour le texte,
  `TemporalChunker` pour les segments) — la sélection dépend de la source.
- Les colonnes `start_time_seconds` / `end_time_seconds` doivent exister en base ; elles ont
  d'abord manqué dans `init.sql` et sont ajoutées via `migrate-temporal-chunks.sql`
  (dette liée à la gestion manuelle du schéma, cf. [ADR-010](010-schema-postgres-scripts-sql.md)).
- La cible ~1400 chars diffère du chunker texte (800) : deux réglages à garder cohérents avec
  la fenêtre du modèle d'embedding.

---

## Alternatives rejetées

| Alternative | Raison du rejet |
|-------------|-----------------|
| Réutiliser `RecursiveChunker` sur la transcription plate | Détruit les timestamps et coupe au milieu des segments. |
| Un chunk = un segment Whisper | Segments trop courts → trop de chunks, embeddings peu informatifs, recherche bruitée. |
| Stocker les timestamps uniquement dans les métadonnées JSON | Non requêtable/indexable proprement ; colonnes typées plus claires et exploitables. |
| Ignorer les timestamps (transcrire en texte plat) | Perd l'atout central de la vidéo : l'ancrage temporel des citations. |
