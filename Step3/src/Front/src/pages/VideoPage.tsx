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

  // Sélectionne l'onglet pertinent dès que la transcription arrive : "Nettoyée" si disponible,
  // sinon "Brute". Recaler l'onglet est volontaire ici — une "Nouvelle transcription" ne remet pas
  // l'onglet à zéro, il faut donc le réaligner sur le contenu réellement reçu.
  useEffect(() => {
    if (!transcription) return;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- réinitialisation intentionnelle à l'arrivée d'une nouvelle transcription
    setActiveTab(transcription.cleanedTranscription ? 'cleaned' : 'raw');
  }, [transcription]);

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
    </div>
  );
}
