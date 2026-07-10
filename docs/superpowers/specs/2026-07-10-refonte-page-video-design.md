# Design — Refonte de la page d'ingestion vidéo (`VideoPage`)

- **Date** : 2026-07-10
- **Périmètre** : Front-end React/TypeScript de Step 3 (`Step3/src/Front`)
- **Objectif** : refondre entièrement la page de transcription vidéo pour la rendre plus soignée
  visuellement et plus agréable à utiliser, sous forme d'assistant par étapes.
- **Contrainte forte** : aucune modification back-end, aucune régression sur les autres pages.

---

## 1. Contexte & diagnostic

La page actuelle (`Step3/src/Front/src/pages/VideoPage.tsx`) s'appuie sur des classes CSS
(`video-form`, `video-upload-zone`, `video-file-selected`, `video-options`, `video-result`,
`video-transcription`, `video-expand-btn`…) **qui ne sont définies nulle part** dans
`Step3/src/Front/src/index.css`. La page rend donc du HTML quasiment sans style : c'est la cause
première de son aspect « brut ».

Autres constats issus de l'exploration :

- Depuis le refactor d'ingestion asynchrone, `VideoTranscriptionResponse` ne renvoie plus que
  `rawTranscription` et `cleanedTranscription`. Les statistiques (durée, nombre de segments, temps
  de traitement) mentionnées dans le CLAUDE.md **ne sont plus disponibles** — le design ne doit pas
  les afficher.
- Le flux est **asynchrone** : `api.video.transcribe` renvoie un `202 Accepted` avec un
  `DocumentResponse` en statut `Pending`. Les changements de statut arrivent ensuite via SignalR
  (hook `useIngestionNotifications`, `subscribe`). La transcription complète n'est récupérée
  (`api.video.getTranscription`) qu'une fois le statut `Completed`.
- L'API `api.video.transcribe` accepte déjà un **titre optionnel** (5ᵉ argument), mais l'UI ne
  l'expose pas aujourd'hui.
- La page Documents (`DocumentsPage.tsx`) fournit des patterns réutilisables (barre de progression,
  toast, styles de boutons) et le design system de `index.css` (variables `--color-*`, `--radius`,
  `--shadow-sm`, `.btn`, `.alert`, `.spinner`, `.progress-bar`).

---

## 2. Décisions validées

- **Périmètre** : *Visuel + UX ciblée*. Refonte visuelle complète (CSS + JSX) **et** trois
  améliorations UX : vrai glisser-déposer, champ Titre optionnel, actions Copier / Télécharger la
  transcription. **Pas** de détection de doublon (spécifique à Documents), **pas** d'historique,
  **pas** de modification back-end.
- **Layout** : *Assistant par étapes* (stepper vertical à 3 étapes).
- **Thème** : mode clair uniquement, comme l'ensemble du front actuel.

---

## 3. Architecture de l'écran — assistant à 3 étapes

Page centrée (`.page`, max 900px) contenant un **indicateur d'étapes** (`1 —— 2 —— 3`) toujours
visible, dont l'étape active est dérivée de l'état interne :

| État interne                                              | Étape active     | Contenu affiché |
|----------------------------------------------------------|------------------|-----------------|
| Aucun fichier, ou fichier sélectionné (avant envoi)      | ① **Fichier**    | Zone de dépôt (drag & drop) + options |
| Envoi en cours, ou document en statut `Pending`/`Processing` | ② **Transcription** | Barre de progression d'envoi, puis carte « traitement en cours » animée |
| Document en statut `Completed`                            | ③ **Résultat**   | Onglets Nettoyée / Brute, actions Copier & Télécharger, CTA vers le Chat |

Les étapes déjà franchies sont marquées d'un ✓. En cas d'échec (`Failed`), on reste sur l'étape ②
avec un message d'erreur et un bouton de reprise.

### Dérivation de l'étape active (règle unique, sans état redondant)

```
if (videoDocument?.status === 'Completed')                    -> étape 3
else if (uploading || status === 'Pending' | 'Processing')    -> étape 2
else                                                          -> étape 1
```

L'étape n'est donc **pas** stockée dans un `useState` dédié : elle est calculée à chaque rendu à
partir de `uploading` et `videoDocument`, ce qui évite toute désynchronisation.

---

## 4. Étape ① — Dépôt & options

### 4.1 Zone de dépôt (drag & drop réel)

- Handlers `onDragOver` / `onDragLeave` / `onDrop` en plus du clic existant.
- Un état `isDragging: boolean` pilote une classe `.video-dropzone-active` (bordure pointillée qui
  s'illumine en `--color-primary`, léger fond teinté).
- `onDrop` récupère `e.dataTransfer.files[0]`, applique la **même validation** que le clic.
- Fichier sélectionné : affiche 🎬 + nom + taille (`formatBytes`, repris de DocumentsPage) et un
  bouton ✕ pour le retirer (réinitialise le champ et l'état).
- Placeholder (aucun fichier) : icône, texte d'invite, liste des formats acceptés.

### 4.2 Validation client

- Extension vérifiée contre `ACCEPTED_EXTENSIONS` (`.mp4,.mkv,.webm,.avi,.mov,.wav,.mp3,.m4a`).
- Si le type n'est pas supporté : message inline `.alert-error`, le fichier n'est pas retenu.
- Fonction `pickFile(file: File | null)` centralise sélection + validation, appelée par le clic et
  par le drop.

### 4.3 Options

Carte d'options lisible :

- **Langue** : `<select>` stylé (fr / en / es / de), défaut `fr`.
- **Titre** (nouveau) : `<input type="text">` optionnel, placeholder explicatif. State `title`.
  Passé à `api.video.transcribe(file, language, cleanWithLlm, autoIngest, title || undefined, …)`.
- **Deux interrupteurs** (switch stylés à partir de `<input type="checkbox">`) :
  - *Nettoyer via LLM* (`cleanWithLlm`, défaut `true`) — micro-description « supprime hésitations,
    structure en paragraphes ».
  - *Indexer dans le RAG* (`autoIngest`, défaut `true`) — micro-description « rend le contenu
    interrogeable dans le Chat ».

### 4.4 Action

- Bouton **Transcrire** (`.btn .btn-primary`), désactivé si aucun fichier ou pendant l'envoi
  (spinner `.btn-spinner` + libellé « Envoi… »).
- Tous les contrôles sont `disabled` pendant `uploading`.

---

## 5. Étape ② — Suivi asynchrone

- **Progression d'envoi** : réutilise `.upload-progress` + `.progress-bar` / `.progress-bar-fill`
  existants, piloté par `uploadProgress` (callback `onProgress` de `api.video.transcribe`).
- **Traitement en cours** : une fois l'envoi terminé et tant que le statut est `Pending` ou
  `Processing`, carte animée (spinner + `statusLabel[status]`) avec le rappel « Vous pouvez continuer
  à naviguer, une notification vous préviendra. »
- **Échec** (`Failed`) : `.alert-error` affichant `errorMessage` (ou message générique par défaut).
- **Bouton « Nouvelle transcription »** : appelle `resetAll()` (voir §7) pour revenir à l'étape ①.
- La logique SignalR existante est **conservée à l'identique** : `subscribe` met à jour
  `videoDocument.status`, et déclenche `getTranscription` au passage en `Completed`.

---

## 6. Étape ③ — Résultat

- **Onglets** Nettoyée / Brute (`.video-tabs`, état `activeTab: 'cleaned' | 'raw'`).
  - Si `cleanedTranscription` est présent : onglet « Nettoyée » actif par défaut.
  - Si absent : seul l'onglet « Brute » est proposé et actif.
- **Bloc texte** : `.video-transcript-text` (fond `--color-surface`, bordure, interligne confortable,
  `max-height` + scroll interne au-delà, `white-space: pre-wrap`).
- **Barre d'actions** :
  - **Copier** : `navigator.clipboard.writeText(...)` sur le texte de l'onglet actif ; feedback
    éphémère « Copié ✓ » (state `copied`, reset via `setTimeout`).
  - **Télécharger** : génère un `Blob` `.txt` téléchargé côté client ; nom dérivé du fichier source
    (ex. `ma-video.txt`), pas d'appel réseau.
  - **Interroger dans le Chat →** : conservé, affiché uniquement si `autoIngest` était activé ;
    `navigate('/chat', { state: { documentId: videoDocument.id } })`.

---

## 7. Détails d'implémentation

### 7.1 `VideoPage.tsx` (réécriture complète)

État local :

- Conservé : `file`, `language`, `cleanWithLlm`, `autoIngest`, `uploading`, `uploadProgress`,
  `videoDocument`, `transcription`, `error`.
- Ajouté : `title: string`, `isDragging: boolean`, `activeTab: 'cleaned' | 'raw'`,
  `copied: boolean`.
- **Supprimé** : `rawExpanded` (remplacé par les onglets).

Fonctions clés :

- `pickFile(file)` — validation + sélection, reset des résultats.
- `resetAll()` — remet l'état initial (nouveau départ).
- `formatBytes(n)` — repris de DocumentsPage.
- `handleCopy()`, `handleDownload()` — actions du résultat.
- `handleSubmit` — inchangé sur le fond ; ajoute `title` à l'appel API.
- `useEffect(subscribe(...))` — **inchangé** (suivi SignalR + `getTranscription` au `Completed`).

Toute nouvelle logique et chaque bloc non trivial sont commentés en français (règle projet).

### 7.2 `index.css` (ajout d'une section, aucune classe existante modifiée)

Nouvelle section `── Video / ingestion ──` en fin de fichier, avec notamment :

- `.video-stepper`, `.video-step`, `.video-step-active`, `.video-step-done`, `.video-step-line`
- `.video-dropzone`, `.video-dropzone-active`, `.video-file-selected`, `.video-file-remove`
- `.video-options`, `.video-field`, `.video-switch` (interrupteur CSS)
- `.video-status-card`
- `.video-tabs`, `.video-tab`, `.video-tab-active`, `.video-transcript-text`, `.video-result-actions`

Contraintes : réutiliser exclusivement les variables et primitives existantes (`--color-*`,
`--radius`, `--shadow-sm`, `.btn`, `.spinner`…) ; **ne modifier aucune classe existante** afin de
garantir zéro régression sur Documents et Chat. Mode clair uniquement.

### 7.3 Ce qui ne change pas

- `api/client.ts`, `types/index.ts`, `IngestionNotificationsContext`, le back-end : intacts.
- Le contrat asynchrone (202 + SignalR + `getTranscription`) est respecté tel quel.

---

## 8. Gestion des erreurs

| Cas | Traitement |
|-----|-----------|
| Extension non supportée | Message inline `.alert-error`, fichier ignoré, on reste à l'étape ① |
| Échec réseau à l'envoi | `catch` → `error` affiché, `uploading` remis à `false` |
| Statut `Failed` reçu via SignalR | `.alert-error` avec `errorMessage`, bouton « Nouvelle transcription » |
| `getTranscription` échoue | `catch` silencieux existant conservé ; le document reste consultable via le Chat |
| `navigator.clipboard` indisponible | `try/catch` autour de `handleCopy`, pas de crash, pas de feedback « Copié » |

---

## 9. Critères de succès

1. La page est entièrement stylée (plus aucune classe CSS orpheline).
2. Le glisser-déposer d'un fichier vidéo/audio fonctionne et déclenche la même validation que le clic.
3. Le champ Titre est transmis à l'API et pris en compte.
4. Les trois étapes s'enchaînent visuellement selon l'état réel (envoi → traitement → résultat).
5. Copier et Télécharger fonctionnent sur la transcription affichée.
6. Le flux asynchrone (SignalR, notification, navigation possible) reste fonctionnel à l'identique.
7. Aucune régression visuelle sur les pages Documents et Chat ; `npm run build` passe sans erreur TS.
