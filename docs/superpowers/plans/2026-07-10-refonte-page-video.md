# Refonte de la page d'ingestion vidéo — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refondre entièrement la page de transcription vidéo (`VideoPage`) sous forme d'assistant à 3 étapes, visuellement soignée, avec glisser-déposer réel, champ Titre optionnel et actions Copier/Télécharger.

**Architecture:** Front-end uniquement. Réécriture complète de `VideoPage.tsx` (logique asynchrone SignalR conservée) + ajout d'une section CSS dédiée dans `index.css` sans toucher aux classes existantes. Aucune modification back-end. L'étape active de l'assistant est **dérivée** de l'état (`uploading`, `videoDocument.status`), jamais stockée.

**Tech Stack:** React 19, TypeScript, Vite, CSS natif (design system par variables `--color-*`). Pas de framework de test dans ce projet : la vérification automatique de chaque tâche = `npm run build` (tsc + vite build) et `npm run lint` ; la vérification fonctionnelle = contrôle visuel via `npm run dev`.

## Global Constraints

- Langue : tout le code produit et **tous les commentaires** sont en français (règle projet).
- Commentaires : chaque composant, fonction et bloc logique non trivial est commenté (JSDoc / `//`), en expliquant le *pourquoi* en langage naturel — **jamais** de référence à un identifiant de tâche/lot dans les commentaires.
- **Ne modifier aucune classe CSS existante** de `index.css` : uniquement ajouter une nouvelle section. Objectif : zéro régression sur les pages Documents et Chat.
- Réutiliser exclusivement les primitives du design system existant : variables `--color-*`, `--radius`, `--shadow-sm`, et classes `.btn`, `.btn-primary`, `.btn-ghost`, `.alert`, `.alert-error`, `.spinner`, `.btn-spinner`, `.upload-progress`, `.progress-bar`.
- Mode clair uniquement (aucun style `prefers-color-scheme`).
- Ne pas modifier `api/client.ts`, `types/index.ts`, ni le back-end. Le contrat asynchrone (202 + SignalR + `getTranscription` au statut `Completed`) est respecté tel quel.
- Répertoire de travail front : `Step3/src/Front`. Toutes les commandes `npm` s'y exécutent.

## Fichiers concernés

| Fichier | Rôle |
|---------|------|
| `Step3/src/Front/src/index.css` | **Modifier** : ajout d'une section `── Video / ingestion ──` en fin de fichier |
| `Step3/src/Front/src/pages/VideoPage.tsx` | **Réécrire** entièrement (logique async conservée) |

Aucun autre fichier n'est créé ou modifié.

---

## Task 1 : Styles de la page vidéo (section CSS complète)

**Files:**
- Modify: `Step3/src/Front/src/index.css` (ajout en fin de fichier uniquement)

**Interfaces:**
- Consumes: variables et primitives existantes de `index.css` (`--color-*`, `--radius`, `--shadow-sm`, `.btn`, `.spinner`…).
- Produces: les classes CSS consommées par la Task 2 :
  `.video-stepper`, `.video-step`, `.video-step-index`, `.video-step-label`, `.video-step-active`, `.video-step-done`, `.video-step-line`,
  `.video-dropzone`, `.video-dropzone-active`, `.video-dropzone-icon`, `.video-dropzone-hint`,
  `.video-file`, `.video-file-icon`, `.video-file-name`, `.video-file-size`, `.video-file-remove`,
  `.video-options`, `.video-field`, `.video-field-label`, `.video-select`, `.video-input`,
  `.video-switch`, `.video-switch-track`, `.video-switch-text`, `.video-switch-title`, `.video-switch-desc`,
  `.video-status-card`,
  `.video-result`, `.video-tabs`, `.video-tab`, `.video-tab-active`, `.video-transcript-text`, `.video-result-actions`.

- [ ] **Step 1 : Ajouter la section CSS complète en fin de `index.css`**

Coller ce bloc **à la fin** de `Step3/src/Front/src/index.css`, sans rien modifier au-dessus :

```css
/* ── Video / ingestion ──────────────────────────────────────────────── */

/* Indicateur d'étapes de l'assistant : 3 pastilles reliées par un trait. */
.video-stepper {
  display: flex;
  align-items: center;
  gap: 0;
  margin-bottom: 28px;
}
.video-step {
  display: flex;
  align-items: center;
  gap: 8px;
  color: var(--color-muted);
  font-size: 13px;
  font-weight: 500;
}
/* Pastille numérotée ; passe en couleur primaire quand l'étape est active ou franchie. */
.video-step-index {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 26px;
  height: 26px;
  border-radius: 50%;
  border: 2px solid var(--color-border);
  background: var(--color-surface);
  font-size: 13px;
  font-weight: 600;
  flex-shrink: 0;
}
.video-step-active .video-step-index {
  border-color: var(--color-primary);
  color: var(--color-primary);
  background: #ede9fe;
}
.video-step-active .video-step-label { color: var(--color-text); }
.video-step-done .video-step-index {
  border-color: var(--color-primary);
  background: var(--color-primary);
  color: #fff;
}
.video-step-done .video-step-label { color: var(--color-text); }
/* Trait de liaison entre deux étapes ; s'illumine quand l'étape précédente est franchie. */
.video-step-line {
  flex: 1;
  height: 2px;
  background: var(--color-border);
  margin: 0 10px;
}
.video-step-line.video-step-line-done { background: var(--color-primary); }

/* Zone de dépôt du fichier (clic + glisser-déposer). */
.video-dropzone {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 8px;
  padding: 40px 24px;
  border: 2px dashed var(--color-border);
  border-radius: var(--radius);
  background: var(--color-surface);
  cursor: pointer;
  text-align: center;
  transition: border-color .15s, background .15s;
}
.video-dropzone:hover { border-color: #c4b5fd; }
/* État "survol pendant un glisser" : bordure et fond teintés en primaire. */
.video-dropzone-active {
  border-color: var(--color-primary);
  background: #f5f3ff;
}
.video-dropzone-icon { font-size: 40px; }
.video-dropzone-hint { font-size: 12px; color: var(--color-muted); }

/* Récapitulatif du fichier sélectionné. */
.video-file {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 14px 16px;
  border: 1px solid var(--color-border);
  border-radius: var(--radius);
  background: var(--color-surface);
  box-shadow: var(--shadow-sm);
}
.video-file-icon { font-size: 26px; flex-shrink: 0; }
.video-file-name { font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.video-file-size { font-size: 12px; color: var(--color-muted); white-space: nowrap; }
.video-file-remove {
  margin-left: auto;
  border: none;
  background: transparent;
  color: var(--color-muted);
  cursor: pointer;
  font-size: 18px;
  line-height: 1;
  padding: 4px 8px;
  border-radius: var(--radius);
}
.video-file-remove:hover { color: var(--color-error); background: var(--color-bg); }

/* Carte des options de transcription. */
.video-options {
  display: flex;
  flex-direction: column;
  gap: 16px;
  margin-top: 16px;
  padding: 20px;
  border: 1px solid var(--color-border);
  border-radius: var(--radius);
  background: var(--color-surface);
  box-shadow: var(--shadow-sm);
}
.video-field { display: flex; flex-direction: column; gap: 6px; }
.video-field-label { font-size: 13px; font-weight: 600; color: var(--color-text); }
.video-select, .video-input {
  padding: 8px 12px;
  border: 1px solid var(--color-border);
  border-radius: var(--radius);
  font-size: 14px;
  background: var(--color-surface);
  color: var(--color-text);
  outline: none;
  transition: border-color .15s;
}
.video-select:focus, .video-input:focus { border-color: var(--color-primary); }

/* Interrupteur (switch) construit à partir d'une checkbox masquée. */
.video-switch {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  cursor: pointer;
}
.video-switch input { position: absolute; opacity: 0; width: 0; height: 0; }
.video-switch-track {
  position: relative;
  flex-shrink: 0;
  width: 40px;
  height: 22px;
  border-radius: 999px;
  background: var(--color-border);
  transition: background .15s;
  margin-top: 2px;
}
.video-switch-track::after {
  content: '';
  position: absolute;
  top: 2px;
  left: 2px;
  width: 18px;
  height: 18px;
  border-radius: 50%;
  background: #fff;
  box-shadow: var(--shadow-sm);
  transition: transform .15s;
}
.video-switch input:checked + .video-switch-track { background: var(--color-primary); }
.video-switch input:checked + .video-switch-track::after { transform: translateX(18px); }
.video-switch input:disabled + .video-switch-track { opacity: .5; }
.video-switch-text { display: flex; flex-direction: column; gap: 2px; }
.video-switch-title { font-size: 14px; font-weight: 500; }
.video-switch-desc { font-size: 12px; color: var(--color-muted); }

/* Carte de suivi du traitement en arrière-plan (étape 2). */
.video-status-card {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 20px;
  border: 1px solid #c4b5fd;
  border-radius: var(--radius);
  background: #ede9fe;
  color: var(--color-primary);
  font-size: 14px;
  font-weight: 500;
}
.video-status-card .spinner { width: 22px; height: 22px; margin: 0; border-width: 2px; }

/* Bloc résultat (étape 3). */
.video-result { display: flex; flex-direction: column; gap: 14px; }
.video-tabs { display: flex; gap: 4px; }
.video-tab {
  padding: 6px 16px;
  border: 1px solid var(--color-border);
  border-radius: var(--radius);
  background: var(--color-bg);
  color: var(--color-muted);
  font-size: 13px;
  font-weight: 500;
  cursor: pointer;
  transition: background .15s, color .15s, border-color .15s;
}
.video-tab:hover { color: var(--color-text); }
.video-tab-active {
  background: #ede9fe;
  color: var(--color-primary);
  border-color: var(--color-primary);
}
.video-transcript-text {
  max-height: 380px;
  overflow-y: auto;
  padding: 16px;
  border: 1px solid var(--color-border);
  border-radius: var(--radius);
  background: var(--color-surface);
  box-shadow: var(--shadow-sm);
  font-size: 14px;
  line-height: 1.7;
  white-space: pre-wrap;
  color: var(--color-text);
}
.video-result-actions { display: flex; flex-wrap: wrap; gap: 8px; }
```

- [ ] **Step 2 : Vérifier que le build passe**

Run (depuis `Step3/src/Front`) : `npm run build`
Expected : succès, aucune erreur TypeScript ni Vite (l'ajout de CSS ne casse rien ; VideoPage utilise encore ses anciennes classes à ce stade, ce qui reste valide).

- [ ] **Step 3 : Commit**

```bash
git add "Step3/src/Front/src/index.css"
git commit -m "style(front): styles de la page de transcription vidéo (assistant par étapes)"
```

---

## Task 2 : Réécriture de `VideoPage` — étape ① (dépôt & options)

**Files:**
- Modify (réécriture): `Step3/src/Front/src/pages/VideoPage.tsx`

**Interfaces:**
- Consumes: classes CSS de la Task 1 ; `api.video.transcribe(file, language, cleanWithLlm, autoIngest, title?, onProgress?)` et `api.video.getTranscription(id)` (`api/client.ts`) ; `useIngestionNotifications()` → `{ registerPendingDocument, subscribe }` ; types `DocumentResponse`, `DocumentStatus`, `VideoTranscriptionResponse`.
- Produces: le composant `VideoPage` complet. À l'issue de cette tâche, les étapes ② et ③ sont rendues par des blocs minimalistes provisoires (voir Step 1) qui seront enrichis en Task 3.

- [ ] **Step 1 : Réécrire `VideoPage.tsx` avec l'étape ① complète et des étapes ②/③ provisoires**

Remplacer intégralement le contenu de `Step3/src/Front/src/pages/VideoPage.tsx` par :

```tsx
import { Fragment, useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import { useIngestionNotifications } from '../context/IngestionNotificationsContext';
import type { DocumentResponse, DocumentStatus, VideoTranscriptionResponse } from '../types';

// Formats audio/vidéo acceptés par le pipeline Whisper/FFmpeg côté back-end.
const ACCEPTED_EXTENSIONS = '.mp4,.mkv,.webm,.avi,.mov,.wav,.mp3,.m4a';
const ACCEPTED_LIST = ACCEPTED_EXTENSIONS.split(',');

// Libellés affichés pour chaque statut d'ingestion pendant le suivi asynchrone.
const statusLabel: Record<DocumentStatus, string> = {
  Pending: 'En file d’attente…',
  Processing: 'Transcription en cours…',
  Completed: 'Terminé',
  Failed: 'Échec',
};

/** Formate une taille en octets de façon lisible (o / Ko / Mo). */
function formatBytes(n: number): string {
  if (n < 1024) return `${n} o`;
  if (n < 1_048_576) return `${(n / 1024).toFixed(1)} Ko`;
  return `${(n / 1_048_576).toFixed(1)} Mo`;
}

export default function VideoPage() {
  // Sélection et options de transcription.
  const [file, setFile] = useState<File | null>(null);
  const [title, setTitle] = useState('');
  const [language, setLanguage] = useState('fr');
  const [cleanWithLlm, setCleanWithLlm] = useState(true);
  const [autoIngest, setAutoIngest] = useState(true);

  // Suivi de l'envoi et du traitement asynchrone.
  const [uploading, setUploading] = useState(false);
  const [uploadProgress, setUploadProgress] = useState(0);
  const [videoDocument, setVideoDocument] = useState<DocumentResponse | null>(null);
  const [transcription, setTranscription] = useState<VideoTranscriptionResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  // État purement visuel : survol de la zone pendant un glisser-déposer, onglet et retour "copié".
  const [isDragging, setIsDragging] = useState(false);
  const [activeTab, setActiveTab] = useState<'cleaned' | 'raw'>('cleaned');
  const [copied, setCopied] = useState(false);

  const fileRef = useRef<HTMLInputElement>(null);
  const navigate = useNavigate();
  const { registerPendingDocument, subscribe } = useIngestionNotifications();

  // L'étape active de l'assistant est dérivée de l'état réel, jamais stockée : cela évite toute
  // désynchronisation entre le suivi asynchrone et l'affichage.
  const processing = videoDocument?.status === 'Pending' || videoDocument?.status === 'Processing';
  const completed = videoDocument?.status === 'Completed';
  const currentStep = completed ? 3 : (uploading || processing || videoDocument?.status === 'Failed') ? 2 : 1;

  // Suit le document créé jusqu'à son état final ; ne recharge la transcription qu'une fois
  // "Completed" — avant cela, RawTranscription/CleanedTranscription sont encore vides côté back-end.
  useEffect(() => subscribe(event => {
    setVideoDocument(prev => {
      if (!prev || prev.id !== event.documentId) return prev;
      return { ...prev, status: event.status, errorMessage: event.errorMessage };
    });

    if (event.documentId === videoDocument?.id && event.status === 'Completed') {
      api.video.getTranscription(event.documentId).then(setTranscription).catch(() => {});
    }
  }), [subscribe, videoDocument?.id]);

  /** Valide l'extension puis retient (ou rejette) le fichier choisi, en réinitialisant les résultats. */
  function pickFile(candidate: File | null) {
    if (!candidate) return;
    const ext = candidate.name.slice(candidate.name.lastIndexOf('.')).toLowerCase();
    if (!ACCEPTED_LIST.includes(ext)) {
      setError(`Format non supporté (${ext}). Formats acceptés : ${ACCEPTED_EXTENSIONS}`);
      return;
    }
    setFile(candidate);
    setError(null);
    setVideoDocument(null);
    setTranscription(null);
  }

  /** Réinitialise complètement la page pour repartir sur une nouvelle transcription. */
  function resetAll() {
    setFile(null);
    setTitle('');
    setUploading(false);
    setUploadProgress(0);
    setVideoDocument(null);
    setTranscription(null);
    setError(null);
    setCopied(false);
    if (fileRef.current) fileRef.current.value = '';
  }

  function handleFileChange(e: React.ChangeEvent<HTMLInputElement>) {
    pickFile(e.target.files?.[0] ?? null);
  }

  // Handlers de glisser-déposer : on n'active le style de survol que si un fichier est réellement glissé.
  function handleDragOver(e: React.DragEvent) {
    e.preventDefault();
    if (!uploading) setIsDragging(true);
  }
  function handleDragLeave(e: React.DragEvent) {
    e.preventDefault();
    setIsDragging(false);
  }
  function handleDrop(e: React.DragEvent) {
    e.preventDefault();
    setIsDragging(false);
    if (uploading) return;
    pickFile(e.dataTransfer.files?.[0] ?? null);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!file) return;

    setUploading(true);
    setUploadProgress(0);
    setError(null);
    setVideoDocument(null);
    setTranscription(null);

    try {
      // 202 Accepted : seul le transfert du fichier est attendu ici — l'extraction audio, la
      // transcription Whisper et le nettoyage LLM éventuel se déroulent en arrière-plan.
      const created = await api.video.transcribe(
        file, language, cleanWithLlm, autoIngest, title.trim() || undefined, setUploadProgress,
      );
      setVideoDocument(created);
      registerPendingDocument(created.id, created.fileName);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setUploading(false);
    }
  }

  return (
    <div className="page">
      <div className="page-header">
        <h1>Transcription Vidéo</h1>
      </div>

      {/* Indicateur des 3 étapes de l'assistant. */}
      <div className="video-stepper">
        {[
          { n: 1, label: 'Fichier' },
          { n: 2, label: 'Transcription' },
          { n: 3, label: 'Résultat' },
        ].map((step, i) => (
          // Fragment nommé porteur de la key : un fragment court `<>` ne peut pas recevoir de key.
          <Fragment key={step.n}>
            {i > 0 && (
              <div className={`video-step-line ${currentStep >= step.n ? 'video-step-line-done' : ''}`} />
            )}
            <div
              className={`video-step ${
                currentStep === step.n ? 'video-step-active' : currentStep > step.n ? 'video-step-done' : ''
              }`}
            >
              <span className="video-step-index">{currentStep > step.n ? '✓' : step.n}</span>
              <span className="video-step-label">{step.label}</span>
            </div>
          </Fragment>
        ))}
      </div>

      {error && <div className="alert alert-error">{error}</div>}

      {/* ── Étape 1 : dépôt du fichier et options ────────────────────────── */}
      {currentStep === 1 && (
        <form onSubmit={handleSubmit}>
          <input
            ref={fileRef}
            type="file"
            accept={ACCEPTED_EXTENSIONS}
            hidden
            onChange={handleFileChange}
          />

          {file ? (
            <div className="video-file">
              <span className="video-file-icon">🎬</span>
              <div style={{ minWidth: 0 }}>
                <div className="video-file-name">{file.name}</div>
                <div className="video-file-size">{formatBytes(file.size)}</div>
              </div>
              <button type="button" className="video-file-remove" onClick={resetAll} aria-label="Retirer le fichier">
                ✕
              </button>
            </div>
          ) : (
            <div
              className={`video-dropzone ${isDragging ? 'video-dropzone-active' : ''}`}
              onClick={() => fileRef.current?.click()}
              onDragOver={handleDragOver}
              onDragLeave={handleDragLeave}
              onDrop={handleDrop}
            >
              <span className="video-dropzone-icon">🎬</span>
              <p>Glissez un fichier vidéo/audio ici, ou cliquez pour parcourir</p>
              <p className="video-dropzone-hint">Formats acceptés : mp4, mkv, webm, avi, mov, wav, mp3, m4a</p>
            </div>
          )}

          <div className="video-options">
            <div className="video-field">
              <label className="video-field-label" htmlFor="video-language">Langue</label>
              <select
                id="video-language"
                className="video-select"
                value={language}
                onChange={e => setLanguage(e.target.value)}
              >
                <option value="fr">Français</option>
                <option value="en">Anglais</option>
                <option value="es">Espagnol</option>
                <option value="de">Allemand</option>
              </select>
            </div>

            <div className="video-field">
              <label className="video-field-label" htmlFor="video-title">Titre (optionnel)</label>
              <input
                id="video-title"
                className="video-input"
                type="text"
                value={title}
                onChange={e => setTitle(e.target.value)}
                placeholder="Titre du document source (défaut : nom du fichier)"
              />
            </div>

            <label className="video-switch">
              <input
                type="checkbox"
                checked={cleanWithLlm}
                onChange={e => setCleanWithLlm(e.target.checked)}
              />
              <span className="video-switch-track" />
              <span className="video-switch-text">
                <span className="video-switch-title">Nettoyer via LLM</span>
                <span className="video-switch-desc">Supprime les hésitations et structure le texte en paragraphes.</span>
              </span>
            </label>

            <label className="video-switch">
              <input
                type="checkbox"
                checked={autoIngest}
                onChange={e => setAutoIngest(e.target.checked)}
              />
              <span className="video-switch-track" />
              <span className="video-switch-text">
                <span className="video-switch-title">Indexer dans le RAG</span>
                <span className="video-switch-desc">Rend le contenu interrogeable depuis le Chat.</span>
              </span>
            </label>
          </div>

          <button
            type="submit"
            className={`btn btn-primary ${!file ? 'btn-disabled' : ''}`}
            disabled={!file}
            style={{ marginTop: 16 }}
          >
            Transcrire
          </button>
        </form>
      )}

      {/* ── Étape 2 : suivi (provisoire, enrichi en Task 3) ──────────────── */}
      {currentStep === 2 && (
        <div className="video-status-card">
          <div className="spinner" />
          {uploading ? `Envoi du fichier — ${uploadProgress}%` : statusLabel[videoDocument!.status]}
        </div>
      )}

      {/* ── Étape 3 : résultat (provisoire, enrichi en Task 3) ───────────── */}
      {currentStep === 3 && transcription && (
        <div className="video-result">
          <div className="video-transcript-text">
            {transcription.cleanedTranscription ?? transcription.rawTranscription}
          </div>
        </div>
      )}
    </div>
  );
}
```

- [ ] **Step 2 : Vérifier le build et le lint**

Run (depuis `Step3/src/Front`) : `npm run build`
Expected : succès sans erreur TypeScript.

Run : `npm run lint`
Expected : aucune erreur. Le `.map` du stepper retourne un `<Fragment key={step.n}>` (importé depuis `react`), ce qui satisfait `react/jsx-key` sans warning.

- [ ] **Step 3 : Vérification visuelle de l'étape ①**

Run : `npm run dev`, ouvrir http://localhost:5173/video.
Vérifier : le stepper affiche 3 étapes (① active) ; la zone de dépôt réagit au survol d'un glisser (bordure violette) ; déposer un `.mp4` affiche nom + taille + bouton ✕ ; déposer un `.txt` affiche l'erreur de format ; les deux interrupteurs basculent ; le champ Titre est saisissable.

- [ ] **Step 4 : Commit**

```bash
git add "Step3/src/Front/src/pages/VideoPage.tsx"
git commit -m "feat(front): refonte de la page vidéo en assistant par étapes (dépôt, drag & drop, options)"
```

---

## Task 3 : Étapes ② (suivi) et ③ (résultat) complètes

**Files:**
- Modify: `Step3/src/Front/src/pages/VideoPage.tsx`

**Interfaces:**
- Consumes: état et fonctions définis en Task 2 (`uploading`, `uploadProgress`, `videoDocument`, `transcription`, `activeTab`, `copied`, `autoIngest`, `file`, `resetAll`, `navigate`).
- Produces: version finale du composant (aucun bloc provisoire restant).

- [ ] **Step 1 : Ajouter les fonctions Copier / Télécharger**

Dans `VideoPage.tsx`, juste après la fonction `resetAll`, ajouter :

```tsx
  /** Renvoie le texte de l'onglet actif (nettoyée si disponible et sélectionnée, sinon brute). */
  function activeText(): string {
    if (!transcription) return '';
    if (activeTab === 'cleaned' && transcription.cleanedTranscription) return transcription.cleanedTranscription;
    return transcription.rawTranscription ?? '';
  }

  /** Copie la transcription affichée dans le presse-papiers, avec un retour visuel éphémère. */
  async function handleCopy() {
    try {
      await navigator.clipboard.writeText(activeText());
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      // Presse-papiers indisponible (contexte non sécurisé) : on échoue silencieusement.
    }
  }

  /** Télécharge la transcription affichée sous forme de fichier .txt, sans appel réseau. */
  function handleDownload() {
    const base = file?.name.replace(/\.[^.]+$/, '') ?? 'transcription';
    const blob = new Blob([activeText()], { type: 'text/plain;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${base}.txt`;
    a.click();
    URL.revokeObjectURL(url);
  }
```

- [ ] **Step 2 : Remplacer le bloc provisoire de l'étape ② par la version complète**

Remplacer le bloc `{currentStep === 2 && ( ... )}` par :

```tsx
      {/* ── Étape 2 : suivi de l'envoi puis du traitement asynchrone ─────── */}
      {currentStep === 2 && (
        <>
          {/* Progression réelle de l'envoi du fichier (seule étape suivie en %). */}
          {uploading && (
            <div className="upload-progress">
              <div className="spinner" />
              <div style={{ flex: 1 }}>
                <div>Envoi du fichier — {uploadProgress}%</div>
                <div className="progress-bar">
                  <div className="progress-bar-fill" style={{ width: `${uploadProgress}%` }} />
                </div>
              </div>
            </div>
          )}

          {/* Traitement en arrière-plan une fois le fichier envoyé. */}
          {!uploading && processing && (
            <div className="video-status-card">
              <div className="spinner" />
              <span>
                {statusLabel[videoDocument!.status]} Vous pouvez continuer à naviguer,
                une notification vous préviendra.
              </span>
            </div>
          )}

          {/* Échec de la transcription : message et possibilité de recommencer. */}
          {videoDocument?.status === 'Failed' && (
            <>
              <div className="alert alert-error">
                {videoDocument.errorMessage ?? 'La transcription a échoué.'}
              </div>
              <button type="button" className="btn btn-primary" onClick={resetAll}>
                Nouvelle transcription
              </button>
            </>
          )}
        </>
      )}
```

- [ ] **Step 3 : Remplacer le bloc provisoire de l'étape ③ par la version complète**

Remplacer le bloc `{currentStep === 3 && transcription && ( ... )}` par :

```tsx
      {/* ── Étape 3 : résultat avec onglets et actions ───────────────────── */}
      {currentStep === 3 && transcription && (
        <div className="video-result">
          {/* Onglets Nettoyée / Brute ; l'onglet Nettoyée n'apparaît que si une version existe. */}
          <div className="video-tabs">
            {transcription.cleanedTranscription && (
              <button
                type="button"
                className={`video-tab ${activeTab === 'cleaned' ? 'video-tab-active' : ''}`}
                onClick={() => setActiveTab('cleaned')}
              >
                Nettoyée
              </button>
            )}
            <button
              type="button"
              className={`video-tab ${activeTab === 'raw' || !transcription.cleanedTranscription ? 'video-tab-active' : ''}`}
              onClick={() => setActiveTab('raw')}
            >
              Brute
            </button>
          </div>

          <div className="video-transcript-text">
            {activeTab === 'cleaned' && transcription.cleanedTranscription
              ? transcription.cleanedTranscription
              : transcription.rawTranscription}
          </div>

          <div className="video-result-actions">
            <button type="button" className="btn btn-ghost" onClick={handleCopy}>
              {copied ? 'Copié ✓' : 'Copier'}
            </button>
            <button type="button" className="btn btn-ghost" onClick={handleDownload}>
              Télécharger .txt
            </button>
            {autoIngest && (
              <button
                type="button"
                className="btn btn-primary"
                onClick={() => navigate('/chat', { state: { documentId: videoDocument!.id } })}
              >
                Interroger ce document dans le Chat →
              </button>
            )}
            <button type="button" className="btn btn-ghost" onClick={resetAll}>
              Nouvelle transcription
            </button>
          </div>
        </div>
      )}
```

- [ ] **Step 4 : Aligner l'onglet par défaut sur la présence d'une version nettoyée**

Pour éviter que l'onglet « Nettoyée » soit actif alors qu'aucune version nettoyée n'existe (cas `cleanWithLlm=false`), ajouter cet effet juste après le `useEffect` d'abonnement SignalR :

```tsx
  // Sélectionne l'onglet pertinent dès que la transcription arrive : "Nettoyée" si disponible,
  // sinon "Brute".
  useEffect(() => {
    if (transcription) setActiveTab(transcription.cleanedTranscription ? 'cleaned' : 'raw');
  }, [transcription]);
```

- [ ] **Step 5 : Vérifier build et lint**

Run (depuis `Step3/src/Front`) : `npm run build`
Expected : succès sans erreur TypeScript.

Run : `npm run lint`
Expected : aucune erreur.

- [ ] **Step 6 : Vérification visuelle du parcours complet**

Prérequis : back-end lancé (`dotnet run --project "Step3/..."`) + base PostgreSQL up, LM Studio/Ollama configuré comme dans `appsettings.json`.
Run : `npm run dev`, http://localhost:5173/video.
Vérifier de bout en bout : déposer un petit fichier audio → l'étape passe à ② (barre d'envoi puis carte « Transcription en cours ») → au `Completed`, l'étape ③ affiche les onglets Nettoyée/Brute ; **Copier** montre « Copié ✓ » ; **Télécharger .txt** produit le fichier ; **Interroger dans le Chat →** navigue avec le `documentId` ; **Nouvelle transcription** ramène à l'étape ①. Tester aussi `cleanWithLlm=false` : seul l'onglet Brute doit apparaître.

- [ ] **Step 7 : Commit**

```bash
git add "Step3/src/Front/src/pages/VideoPage.tsx"
git commit -m "feat(front): suivi asynchrone et résultat (onglets, copier, télécharger) de la page vidéo"
```

---

## Task 4 : Vérification finale de non-régression

**Files:** aucun (vérification seule).

- [ ] **Step 1 : Build de production complet**

Run (depuis `Step3/src/Front`) : `npm run build`
Expected : succès. Confirme que tsc + Vite compilent l'ensemble sans erreur.

- [ ] **Step 2 : Contrôle visuel des pages voisines**

Run : `npm run dev`. Ouvrir `/` (Documents) et `/chat` (Chat).
Expected : aucune régression visuelle (les classes existantes n'ont pas été modifiées). Vérifier notamment la barre d'upload, les badges de statut, la mise en page du chat.

- [ ] **Step 3 : Vérifier l'absence de classes CSS orphelines dans VideoPage**

Vérifier que toutes les classes `video-*` utilisées dans `VideoPage.tsx` existent dans la section CSS ajoutée en Task 1 (aucune classe rendue sans style). Critère de succès n°1 de la spec.

---

## Self-Review — couverture de la spec

- **§1 Diagnostic (classes orphelines)** → Task 1 (styles) + Task 4 Step 3 (contrôle).
- **§3 Assistant 3 étapes, étape dérivée** → Task 2 (`currentStep` calculé) + stepper.
- **§4 Dépôt drag & drop, validation, options, titre** → Task 2.
- **§5 Suivi asynchrone, échec, reprise** → Task 3 Step 2.
- **§6 Résultat : onglets, copier, télécharger, CTA Chat** → Task 3 Steps 1/3/4.
- **§7 Détails d'implémentation (state ajouté/supprimé, SignalR inchangé, CSS additif)** → Tasks 1–3.
- **§8 Gestion des erreurs (format, réseau, Failed, clipboard indispo)** → Task 2 (`pickFile`, `catch` submit) + Task 3 (`handleCopy` try/catch, bloc Failed).
- **§9 Critères de succès** → couverts par les vérifications des Tasks 2, 3 et 4.

Aucun placeholder ; noms de classes et signatures cohérents entre tâches (`resetAll`, `pickFile`, `activeText`, `currentStep`, classes `video-*`).
