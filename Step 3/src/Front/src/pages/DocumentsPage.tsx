import { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import type { DocumentResponse } from '../types';

export default function DocumentsPage() {
  const navigate = useNavigate();
  const [documents, setDocuments] = useState<DocumentResponse[]>([]);
  const [loading, setLoading] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [toast, setToast] = useState<string | null>(null);
  const fileRef = useRef<HTMLInputElement>(null);
  const checkAllRef = useRef<HTMLInputElement>(null);

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

  useEffect(() => {
    if (!checkAllRef.current) return;
    checkAllRef.current.indeterminate = selected.size > 0 && selected.size < documents.length;
  }, [selected.size, documents.length]);

  function showToast(msg: string) {
    setToast(msg);
    setTimeout(() => setToast(null), 3500);
  }

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

  function toggleSelect(id: string) {
    setSelected(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  function toggleAll() {
    if (selected.size === documents.length) {
      setSelected(new Set());
    } else {
      setSelected(new Set(documents.map(d => d.id)));
    }
  }

  async function handleDeleteSelected() {
    const count = selected.size;
    const ids = [...selected];
    setDeleting(true);
    setError(null);
    try {
      await Promise.all(ids.map(id => api.documents.delete(id)));
      setDocuments(prev => prev.filter(d => !ids.includes(d.id)));
      setSelected(new Set());
      setConfirmOpen(false);
      showToast(
        count === 1
          ? 'Document supprimé avec succès'
          : `${count} documents supprimés avec succès`
      );
    } catch (err) {
      setError((err as Error).message);
      setConfirmOpen(false);
    } finally {
      setDeleting(false);
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
        <div className="page-header-actions">
          {selected.size > 0 && (
            <button className="btn btn-danger" onClick={() => setConfirmOpen(true)}>
              Supprimer ({selected.size})
            </button>
          )}
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
              <th className="col-check">
                <input
                  ref={checkAllRef}
                  type="checkbox"
                  checked={selected.size === documents.length && documents.length > 0}
                  onChange={toggleAll}
                />
              </th>
              <th>Fichier</th>
              <th>Taille</th>
              <th>Statut</th>
              <th>Ajouté le</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {documents.map(doc => (
              <tr
                key={doc.id}
                className={selected.has(doc.id) ? 'row-selected' : ''}
                onClick={() => toggleSelect(doc.id)}
              >
                <td className="col-check" onClick={e => e.stopPropagation()}>
                  <input
                    type="checkbox"
                    checked={selected.has(doc.id)}
                    onChange={() => toggleSelect(doc.id)}
                  />
                </td>
                <td className="filename">{doc.fileName}</td>
                <td>{formatBytes(doc.fileSizeBytes)}</td>
                <td>
                  <span className={`badge ${statusColor[doc.status] ?? ''}`}>
                    {doc.status}
                  </span>
                </td>
                <td>{new Date(doc.createdAt).toLocaleDateString('fr-FR')}</td>
                <td className="col-actions" onClick={e => e.stopPropagation()}>
                  {doc.status === 'Completed' && (
                    <button
                      className="btn btn-ghost btn-sm"
                      onClick={() => navigate('/chat', { state: { documentId: doc.id, documentName: doc.fileName } })}
                    >
                      Chat →
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {confirmOpen && (
        <div className="modal-overlay" onClick={() => !deleting && setConfirmOpen(false)}>
          <div className="modal" onClick={e => e.stopPropagation()}>
            <h2>Confirmer la suppression</h2>
            <p>
              {selected.size === 1
                ? 'Voulez-vous vraiment supprimer ce document ?'
                : `Voulez-vous vraiment supprimer ces ${selected.size} documents ?`}
            </p>
            <p className="modal-warning">Cette action est irréversible.</p>
            <div className="modal-actions">
              <button
                className="btn btn-ghost"
                onClick={() => setConfirmOpen(false)}
                disabled={deleting}
              >
                Annuler
              </button>
              <button
                className="btn btn-danger"
                onClick={handleDeleteSelected}
                disabled={deleting}
              >
                {deleting && <span className="btn-spinner" />}
                {deleting ? 'Suppression…' : 'Supprimer'}
              </button>
            </div>
          </div>
        </div>
      )}

      {toast && (
        <div className="toast toast-success">
          ✓ {toast}
        </div>
      )}
    </div>
  );
}
