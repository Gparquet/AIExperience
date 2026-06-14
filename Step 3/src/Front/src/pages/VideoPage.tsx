import { useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import type { TranscribeVideoResponse } from '../types';

const ACCEPTED_EXTENSIONS = '.mp4,.mkv,.webm,.avi,.mov,.wav,.mp3,.m4a';

export default function VideoPage() {
  const [file, setFile] = useState<File | null>(null);
  const [language, setLanguage] = useState('fr');
  const [cleanWithLlm, setCleanWithLlm] = useState(true);
  const [autoIngest, setAutoIngest] = useState(true);
  const [processing, setProcessing] = useState(false);
  const [result, setResult] = useState<TranscribeVideoResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [rawExpanded, setRawExpanded] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);
  const navigate = useNavigate();

  function handleFileChange(e: React.ChangeEvent<HTMLInputElement>) {
    const f = e.target.files?.[0] ?? null;
    setFile(f);
    setResult(null);
    setError(null);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!file) return;

    setProcessing(true);
    setError(null);
    setResult(null);

    try {
      const response = await api.video.transcribe(file, language, cleanWithLlm, autoIngest);
      setResult(response);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setProcessing(false);
    }
  }

  function formatDuration(ts: string): string {
    // TimeSpan sérialisé "hh:mm:ss.fffffff" — on garde hh:mm:ss
    return ts.split('.')[0];
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
            disabled={processing}
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
              disabled={processing}
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
              disabled={processing}
            />
            <span>Nettoyer via LLM (supprime hésitations, structure en paragraphes)</span>
          </label>

          <label className="video-option video-option-checkbox">
            <input
              type="checkbox"
              checked={autoIngest}
              onChange={e => setAutoIngest(e.target.checked)}
              disabled={processing}
            />
            <span>Indexer dans le RAG (rend le contenu interrogeable)</span>
          </label>
        </div>

        <button
          type="submit"
          className={`btn btn-primary ${(!file || processing) ? 'btn-disabled' : ''}`}
          disabled={!file || processing}
        >
          {processing && <span className="btn-spinner" />}
          {processing ? 'Transcription en cours…' : 'Transcrire'}
        </button>
      </form>

      {/* Message d'attente */}
      {processing && (
        <div className="upload-progress">
          <div className="spinner" />
          Extraction audio et transcription Whisper en cours — cela peut prendre quelques instants…
        </div>
      )}

      {/* Erreur */}
      {error && <div className="alert alert-error">{error}</div>}

      {/* Résultat */}
      {result && (
        <div className="video-result">
          {/* Statistiques */}
          <div className="video-stats">
            <div className="video-stat">
              <span className="video-stat-label">Durée</span>
              <span className="video-stat-value">{formatDuration(result.duration)}</span>
            </div>
            <div className="video-stat">
              <span className="video-stat-label">Segments</span>
              <span className="video-stat-value">{result.segmentCount}</span>
            </div>
            <div className="video-stat">
              <span className="video-stat-label">Traitement</span>
              <span className="video-stat-value">{formatDuration(result.processingTime)}</span>
            </div>
          </div>

          {/* Transcription nettoyée (prioritaire) */}
          {result.cleanedTranscription && (
            <div className="video-transcription">
              <h3>Transcription nettoyée</h3>
              <pre className="video-transcription-text">{result.cleanedTranscription}</pre>
            </div>
          )}

          {/* Transcription brute (repliable) */}
          <div className="video-transcription">
            <button
              className="video-expand-btn"
              onClick={() => setRawExpanded(v => !v)}
            >
              {rawExpanded ? '▲' : '▼'} Transcription brute avec timestamps
            </button>
            {rawExpanded && (
              <pre className="video-transcription-text video-transcription-raw">
                {result.rawTranscription}
              </pre>
            )}
          </div>

          {/* Bouton vers le chat */}
          {result.documentId && (
            <div className="video-actions">
              <button
                className="btn btn-primary"
                onClick={() => navigate('/chat', { state: { documentId: result.documentId } })}
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
