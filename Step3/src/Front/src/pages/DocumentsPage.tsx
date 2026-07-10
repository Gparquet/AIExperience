import { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import { useIngestionNotifications } from '../context/IngestionNotificationsContext';
import type { DocumentMatchType, DocumentResponse, DocumentStatus, ExistingDocumentInfo } from '../types';

// Formats supportés côté back-end (CompositeTextExtractor).
const ACCEPTED_EXTENSIONS =
  '.pdf,.html,.htm,.docx,.xlsx,.csv,.pptx,.txt,.md,.json,.mp4,.mkv,.webm,.avi,.mov,.wav,.mp3,.m4a,.ogg,.flac';

const statusColor: Record<DocumentStatus, string> = {
  Completed: 'badge-success',
  Pending: 'badge-warning',
  Processing: 'badge-info',
  Failed: 'badge-error',
};

// Le back-end n'introduit pas de nouveau statut pour la progression fine — on relabellise
// seulement à l'affichage les statuts existants (Pending/Processing) pour parler en file d'attente.
const statusLabel: Record<DocumentStatus, string> = {
  Pending: 'En file d’attente',
  Processing: 'Traitement en cours…',
  Completed: 'Terminé',
  Failed: 'Échec',
};

export default function DocumentsPage() {
  const navigate = useNavigate();
  const { registerPendingDocument, subscribe } = useIngestionNotifications();
  const [documents, setDocuments] = useState<DocumentResponse[]>([]);
  const [loading, setLoading] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [uploadProgress, setUploadProgress] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [toast, setToast] = useState<string | null>(null);
  // Doublon ou nouvelle version détecté(e) en attente de confirmation utilisateur.
  const [pendingDuplicate, setPendingDuplicate] = useState<{ file: File; existing: ExistingDocumentInfo; matchType: DocumentMatchType } | null>(null);
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

  // Patch en place le statut d'un document dès qu'il change (notification temps réel ou polling
  // de repli), sans recharger toute la liste.
  useEffect(() => subscribe(event => {
    setDocuments(prev => prev.map(doc =>
      doc.id === event.documentId ? { ...doc, status: event.status, errorMessage: event.errorMessage } : doc));
  }), [subscribe]);

  useEffect(() => {
    if (!checkAllRef.current) return;
    checkAllRef.current.indeterminate = selected.size > 0 && selected.size < documents.length;
  }, [selected.size, documents.length]);

  function showToast(msg: string) {
    setToast(msg);
    setTimeout(() => setToast(null), 3500);
  }

  /** Calcule le SHA-256 (hex minuscule) d'un fichier côté navigateur, pour la pré-vérification de doublon. */
  async function computeSha256(file: File): Promise<string> {
    const buffer = await file.arrayBuffer();
    const hashBuffer = await crypto.subtle.digest('SHA-256', buffer);
    return Array.from(new Uint8Array(hashBuffer))
      .map(b => b.toString(16).padStart(2, '0'))
      .join('');
  }

  async function handleUpload(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (!file) return;
    setUploading(true);
    setUploadProgress(0);
    setError(null);
    try {
      // Si crypto.subtle est indisponible (contexte non sécurisé), on saute la pré-vérification :
      // le 409 renvoyé par le back-end (garde-fou serveur) reste la protection en dernier recours.
      let hash: string | null = null;
      try {
        hash = await computeSha256(file);
      } catch {
        hash = null;
      }

      if (hash) {
        const check = await api.documents.checkDuplicate(file.name, hash);
        if (check.isDuplicate && check.existingDocument && check.matchType) {
          setPendingDuplicate({ file, existing: check.existingDocument, matchType: check.matchType });
          return;
        }
      }

      // 202 Accepted : seul le transfert du fichier est attendu ici, l'ingestion elle-même
      // continue en arrière-plan — d'où le déblocage immédiat du formulaire ci-dessous.
      const created = await api.documents.upload(file, 'Recursive', undefined, setUploadProgress);
      setDocuments(prev => [created, ...prev]);
      registerPendingDocument(created.id, created.fileName);
    } catch (err) {
      // Repli si le doublon n'a pas été intercepté par la pré-vérification (ex. crypto.subtle
      // indisponible) : le back-end renvoie alors un 409 brut qu'on remplace par un message générique.
      // Générique à dessein : ce 409 peut correspondre à un doublon exact ou à une nouvelle version
      // (contenu différent), cas que ce message de repli ne distingue pas.
      const status = (err as Error & { status?: number }).status;
      setError(status === 409 ? 'Un document portant ce nom existe déjà.' : (err as Error).message);
    } finally {
      setUploading(false);
      if (fileRef.current) fileRef.current.value = '';
    }
  }

  /** Confirme le remplacement du document existant par le nouveau fichier (doublon détecté). */
  async function handleConfirmReplace() {
    if (!pendingDuplicate) return;
    const { file, existing } = pendingDuplicate;
    setUploading(true);
    setUploadProgress(0);
    setError(null);
    try {
      const created = await api.documents.upload(file, 'Recursive', existing.id, setUploadProgress);
      setDocuments(prev => [created, ...prev.filter(d => d.id !== existing.id)]);
      registerPendingDocument(created.id, created.fileName);
    } catch (err) {
      // Même repli que dans handleUpload : un 409 ici signifierait une nouvelle collision
      // détectée par le back-end au moment du remplacement (cas rare).
      const status = (err as Error & { status?: number }).status;
      setError(status === 409 ? 'Un document portant ce nom existe déjà.' : (err as Error).message);
    } finally {
      setUploading(false);
      setPendingDuplicate(null);
      if (fileRef.current) fileRef.current.value = '';
    }
  }

  /** Annule le remplacement : ferme la popup sans appel réseau. */
  function handleCancelReplace() {
    setPendingDuplicate(null);
    if (fileRef.current) fileRef.current.value = '';
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
            {uploading ? 'Envoi…' : '+ Ajouter un document'}
            <input
              ref={fileRef}
              type="file"
              accept={ACCEPTED_EXTENSIONS}
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
          <div style={{ flex: 1 }}>
            <div>Envoi du fichier — {uploadProgress}%</div>
            <div className="progress-bar">
              <div className="progress-bar-fill" style={{ width: `${uploadProgress}%` }} />
            </div>
          </div>
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
                  <span className={`badge ${statusColor[doc.status]}`} title={doc.errorMessage ?? undefined}>
                    {statusLabel[doc.status]}
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

      {pendingDuplicate && (
        <div className="modal-overlay" onClick={() => !uploading && handleCancelReplace()}>
          <div className="modal" onClick={e => e.stopPropagation()}>
            {pendingDuplicate.matchType === 'ExactDuplicate' ? (
              <>
                <h2>Document déjà importé</h2>
                <p>
                  Un document nommé « {pendingDuplicate.existing.fileName} » a déjà été importé le{' '}
                  {new Date(pendingDuplicate.existing.createdAt).toLocaleDateString('fr-FR')}.
                </p>
                <p className="modal-warning">
                  Si vous continuez, ce document existant sera supprimé et remplacé par le nouveau fichier.
                </p>
              </>
            ) : (
              <>
                <h2>Nouvelle version détectée</h2>
                <p>
                  Une version différente de « {pendingDuplicate.existing.fileName} » a déjà été importée le{' '}
                  {new Date(pendingDuplicate.existing.createdAt).toLocaleDateString('fr-FR')}. Le contenu a changé.
                </p>
                <p className="modal-warning">
                  Si vous continuez, l'ancienne version sera supprimée et remplacée par ce nouveau fichier.
                </p>
              </>
            )}
            <div className="modal-actions">
              <button className="btn btn-ghost" onClick={handleCancelReplace} disabled={uploading}>
                Annuler
              </button>
              <button className="btn btn-danger" onClick={handleConfirmReplace} disabled={uploading}>
                {uploading && <span className="btn-spinner" />}
                {uploading ? 'Remplacement…' : 'Confirmer le remplacement'}
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
