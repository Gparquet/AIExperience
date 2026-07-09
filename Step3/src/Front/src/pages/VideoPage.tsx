import { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import { useIngestionNotifications } from '../context/IngestionNotificationsContext';
import type { DocumentResponse, DocumentStatus, VideoTranscriptionResponse } from '../types';

const ACCEPTED_EXTENSIONS = '.mp4,.mkv,.webm,.avi,.mov,.wav,.mp3,.m4a';

const statusLabel: Record<DocumentStatus, string> = {
  Pending: 'En file d’attente…',
  Processing: 'Transcription en cours…',
  Completed: 'Terminé',
  Failed: 'Échec',
};

export default function VideoPage() {
  const [file, setFile] = useState<File | null>(null);
  const [language, setLanguage] = useState('fr');
  const [cleanWithLlm, setCleanWithLlm] = useState(true);
  const [autoIngest, setAutoIngest] = useState(true);
  const [uploading, setUploading] = useState(false);
  const [uploadProgress, setUploadProgress] = useState(0);
  const [videoDocument, setVideoDocument] = useState<DocumentResponse | null>(null);
  const [transcription, setTranscription] = useState<VideoTranscriptionResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [rawExpanded, setRawExpanded] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);
  const navigate = useNavigate();
  const { registerPendingDocument, subscribe } = useIngestionNotifications();

  function handleFileChange(e: React.ChangeEvent<HTMLInputElement>) {
    const f = e.target.files?.[0] ?? null;
    setFile(f);
    setVideoDocument(null);
    setTranscription(null);
    setError(null);
  }

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
      const created = await api.video.transcribe(file, language, cleanWithLlm, autoIngest, undefined, setUploadProgress);
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

      <form onSubmit={handleSubmit} className="video-form">
        {/* Zone de sélection du fichier */}
        <div className="video-upload-zone" onClick={() => fileRef.current?.click()}>
          <input
            ref={fileRef}
            type="file"
            accept={ACCEPTED_EXTENSIONS}
            hidden
            onChange={handleFileChange}
            disabled={uploading}
          />
          {file ? (
            <div className="video-file-selected">
              <span className="video-file-icon">🎬</span>
              <span className="video-file-name">{file.name}</span>
              <span className="video-file-size">
                {(file.size / 1_048_576).toFixed(1)} Mo
              </span>
            </div>
          ) : (
            <div className="video-file-placeholder">
              <span className="video-file-icon">📁</span>
              <p>Cliquez ou déposez un fichier vidéo/audio ici</p>
              <p className="video-file-hint">
                Formats acceptés : mp4, mkv, webm, avi, mov, wav, mp3, m4a
              </p>
            </div>
          )}
        </div>

        {/* Options */}
        <div className="video-options">
          <label className="video-option">
            <span>Langue</span>
            <select
              value={language}
              onChange={e => setLanguage(e.target.value)}
              disabled={uploading}
            >
              <option value="fr">Français</option>
              <option value="en">Anglais</option>
              <option value="es">Espagnol</option>
              <option value="de">Allemand</option>
            </select>
          </label>

          <label className="video-option video-option-checkbox">
            <input
              type="checkbox"
              checked={cleanWithLlm}
              onChange={e => setCleanWithLlm(e.target.checked)}
              disabled={uploading}
            />
            <span>Nettoyer via LLM (supprime hésitations, structure en paragraphes)</span>
          </label>

          <label className="video-option video-option-checkbox">
            <input
              type="checkbox"
              checked={autoIngest}
              onChange={e => setAutoIngest(e.target.checked)}
              disabled={uploading}
            />
            <span>Indexer dans le RAG (rend le contenu interrogeable)</span>
          </label>
        </div>

        <button
          type="submit"
          className={`btn btn-primary ${(!file || uploading) ? 'btn-disabled' : ''}`}
          disabled={!file || uploading}
        >
          {uploading && <span className="btn-spinner" />}
          {uploading ? 'Envoi…' : 'Transcrire'}
        </button>
      </form>

      {/* Progression de l'envoi du fichier (seule étape réellement suivie en pourcentage — la
          transcription elle-même se déroule hors requête, son avancement fin n'est pas exposé). */}
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

      {/* Statut du traitement en arrière-plan, une fois le fichier envoyé */}
      {videoDocument && (videoDocument.status === 'Pending' || videoDocument.status === 'Processing') && (
        <div className="upload-progress">
          <div className="spinner" />
          {statusLabel[videoDocument.status]} Vous pouvez continuer à naviguer, une notification vous préviendra.
        </div>
      )}

      {/* Erreur */}
      {error && <div className="alert alert-error">{error}</div>}
      {videoDocument?.status === 'Failed' && (
        <div className="alert alert-error">{videoDocument.errorMessage ?? 'La transcription a échoué.'}</div>
      )}

      {/* Résultat */}
      {videoDocument?.status === 'Completed' && transcription && (
        <div className="video-result">
          {/* Transcription nettoyée (prioritaire) */}
          {transcription.cleanedTranscription && (
            <div className="video-transcription">
              <h3>Transcription nettoyée</h3>
              <pre className="video-transcription-text">{transcription.cleanedTranscription}</pre>
            </div>
          )}

          {/* Transcription brute (repliable) */}
          <div className="video-transcription">
            <button
              className="video-expand-btn"
              onClick={() => setRawExpanded(v => !v)}
            >
              {rawExpanded ? '▲' : '▼'} Transcription brute
            </button>
            {rawExpanded && (
              <pre className="video-transcription-text video-transcription-raw">
                {transcription.rawTranscription}
              </pre>
            )}
          </div>

          {/* Bouton vers le chat */}
          {autoIngest && (
            <div className="video-actions">
              <button
                className="btn btn-primary"
                onClick={() => navigate('/chat', { state: { documentId: videoDocument.id } })}
              >
                Interroger ce document dans le Chat →
              </button>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
