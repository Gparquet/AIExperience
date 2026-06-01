import { useEffect, useRef, useState } from 'react';
import { api } from '../api/client';
import type { DocumentResponse } from '../types';

export default function DocumentsPage() {
  const [documents, setDocuments] = useState<DocumentResponse[]>([]);
  const [loading, setLoading] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const fileRef = useRef<HTMLInputElement>(null);

  async function loadDocuments() {
    setLoading(true);
    setError(null);
    try {
      setDocuments(await api.documents.list());
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { loadDocuments(); }, []);

  async function handleUpload(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (!file) return;
    setUploading(true);
    setError(null);
    try {
      await api.documents.upload(file);
      await loadDocuments();
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setUploading(false);
      if (fileRef.current) fileRef.current.value = '';
    }
  }

  async function handleDelete(id: string, name: string) {
    if (!confirm(`Supprimer « ${name} » ?`)) return;
    try {
      await api.documents.delete(id);
      setDocuments(prev => prev.filter(d => d.id !== id));
    } catch (err) {
      setError((err as Error).message);
    }
  }

  function formatBytes(n: number) {
    if (n < 1024) return `${n} o`;
    if (n < 1_048_576) return `${(n / 1024).toFixed(1)} Ko`;
    return `${(n / 1_048_576).toFixed(1)} Mo`;
  }

  const statusColor: Record<string, string> = {
    Completed: 'badge-success',
    Pending: 'badge-warning',
    Processing: 'badge-info',
    Failed: 'badge-error',
  };

  return (
    <div className="page">
      <div className="page-header">
        <h1>Documents</h1>
        <label className={`btn btn-primary ${uploading ? 'btn-disabled' : ''}`}>
          {uploading && <span className="btn-spinner" />}
          {uploading ? 'Importation…' : '+ Ajouter un PDF'}
          <input
            ref={fileRef}
            type="file"
            accept=".pdf"
            hidden
            onChange={handleUpload}
            disabled={uploading}
          />
        </label>
      </div>

      {error && <div className="alert alert-error">{error}</div>}

      {uploading && (
        <div className="upload-progress">
          <div className="spinner" />
          Importation et traitement du document en cours…
        </div>
      )}

      {loading ? (
        <div className="spinner" />
      ) : documents.length === 0 ? (
        <div className="empty-state">
          <span>📄</span>
          <p>Aucun document. Ajoutez un PDF pour commencer.</p>
        </div>
      ) : (
        <table className="table">
          <thead>
            <tr>
              <th>Fichier</th>
              <th>Taille</th>
              <th>Statut</th>
              <th>Ajouté le</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {documents.map(doc => (
              <tr key={doc.id}>
                <td className="filename">{doc.fileName}</td>
                <td>{formatBytes(doc.fileSizeBytes)}</td>
                <td>
                  <span className={`badge ${statusColor[doc.status] ?? ''}`}>
                    {doc.status}
                  </span>
                </td>
                <td>{new Date(doc.createdAt).toLocaleDateString('fr-FR')}</td>
                <td>
                  <button
                    className="btn btn-ghost btn-sm btn-delete"
                    onClick={() => handleDelete(doc.id, doc.fileName)}
                    title="Supprimer"
                  >
                    🗑
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
