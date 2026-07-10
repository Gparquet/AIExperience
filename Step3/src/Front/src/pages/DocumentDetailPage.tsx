import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { api } from '../api/client';
import IngestionStepper from '../components/IngestionStepper';
import { useIngestionNotifications } from '../context/IngestionNotificationsContext';
import type { DocumentResponse, IngestionProgress } from '../types';

const statusLabel: Record<string, string> = {
  Pending: 'En file d’attente', Processing: 'Traitement en cours…', Completed: 'Terminé', Failed: 'Échec',
};
const statusColor: Record<string, string> = {
  Pending: 'badge-warning', Processing: 'badge-info', Completed: 'badge-success', Failed: 'badge-error',
};

function formatBytes(n: number) {
  if (n < 1024) return `${n} o`;
  if (n < 1_048_576) return `${(n / 1024).toFixed(1)} Ko`;
  return `${(n / 1_048_576).toFixed(1)} Mo`;
}

/** Page de détail d'un document : suivi fin d'ingestion en temps réel + métadonnées + transcription. */
export default function DocumentDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { subscribe, subscribeProgress } = useIngestionNotifications();

  const [doc, setDoc] = useState<DocumentResponse | null>(null);
  const [progress, setProgress] = useState<IngestionProgress | null>(null);
  const [loading, setLoading] = useState(true);
  const [notFound, setNotFound] = useState(false);
  const [transcription, setTranscription] = useState<string | null>(null);

  // Chargement initial : document + avancement persisté à l'instant t.
  useEffect(() => {
    if (!id) return;
    setLoading(true);
    api.documents.get(id)
      .then(d => { setDoc(d); setProgress(d.ingestionProgress ?? null); })
      .catch(() => setNotFound(true))
      .finally(() => setLoading(false));
  }, [id]);

  // Statut grossier en temps réel (patch en place).
  useEffect(() => {
    if (!id) return;
    return subscribe(event => {
      if (event.documentId !== id) return;
      setDoc(prev => prev ? { ...prev, status: event.status, errorMessage: event.errorMessage } : prev);
    });
  }, [id, subscribe]);

  // Avancement fin en temps réel (filtré par id).
  useEffect(() => {
    if (!id) return;
    return subscribeProgress(id, event => {
      setProgress({ stage: event.stage, percent: event.percent, counters: event.counters, updatedAt: new Date().toISOString() });
    });
  }, [id, subscribeProgress]);

  // Une fois terminé pour une vidéo/audio, on récupère la transcription pour l'aperçu.
  useEffect(() => {
    if (!id || !doc || doc.status !== 'Completed') return;
    api.video.getTranscription(id)
      .then(t => setTranscription(t.cleanedTranscription ?? t.rawTranscription ?? null))
      .catch(() => { /* document non vidéo : pas de transcription, on ignore */ });
  }, [id, doc]);

  if (loading) return <div className="page"><div className="spinner" /></div>;
  if (notFound || !doc) {
    return (
      <div className="page">
        <div className="empty-state">
          <span>🔍</span>
          <p>Document introuvable.</p>
          <button className="btn btn-ghost" onClick={() => navigate('/')}>← Retour aux documents</button>
        </div>
      </div>
    );
  }

  return (
    <div className="page">
      <div className="page-header">
        <div>
          <button className="btn btn-ghost btn-sm" onClick={() => navigate('/')}>← Documents</button>
          <h1 className="detail-title">{doc.fileName}</h1>
        </div>
        <span className={`badge ${statusColor[doc.status]}`}>{statusLabel[doc.status]}</span>
      </div>

      <div className="detail-meta">
        <div><span className="detail-meta-label">Type</span><span>{doc.contentType}</span></div>
        <div><span className="detail-meta-label">Taille</span><span>{formatBytes(doc.fileSizeBytes)}</span></div>
        <div><span className="detail-meta-label">Ajouté le</span><span>{new Date(doc.createdAt).toLocaleString('fr-FR')}</span></div>
      </div>

      <section className="detail-section">
        <h2>Pipeline d'ingestion</h2>
        <IngestionStepper status={doc.status} contentType={doc.contentType} fileName={doc.fileName} progress={progress} />
      </section>

      {doc.status === 'Failed' && doc.errorMessage && (
        <div className="alert alert-error">{doc.errorMessage}</div>
      )}

      {doc.status === 'Completed' && transcription && (
        <section className="detail-section">
          <h2>Transcription</h2>
          <pre className="transcription-preview">{transcription}</pre>
        </section>
      )}

      {doc.status === 'Completed' && (
        <button
          className="btn btn-primary"
          onClick={() => navigate('/chat', { state: { documentId: doc.id, documentName: doc.fileName } })}
        >
          Interroger ce document dans le Chat →
        </button>
      )}
    </div>
  );
}
