import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import type { ChatSessionSummary } from '../types';

/**
 * Liste latérale des conversations. Se recharge au montage et à chaque incrément de `reloadSignal`
 * (déclenché par ChatPage après un nouvel échange, pour remonter la conversation active en tête).
 */
interface ChatSidebarProps {
  /** Id de la conversation actuellement ouverte (surlignée), ou undefined pour une conversation vierge. */
  activeSessionId?: string;
  /** Compteur : toute incrémentation force un rechargement de la liste. */
  reloadSignal: number;
}

// Cible d'une suppression en attente de confirmation : un item précis, ou 'all' pour tout supprimer.
type PendingDelete = { id: string; title: string } | 'all';

export default function ChatSidebar({ activeSessionId, reloadSignal }: ChatSidebarProps) {
  const navigate = useNavigate();
  const [sessions, setSessions] = useState<ChatSessionSummary[]>([]);
  const [pendingDelete, setPendingDelete] = useState<PendingDelete | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  // Rechargement de la liste au montage puis à chaque nouvel échange (reloadSignal).
  useEffect(() => {
    api.chat.listSessions()
      .then(setSessions)
      .catch(() => { /* la sidebar reste vide si l'API est indisponible — non bloquant */ });
  }, [reloadSignal]);

  /** Confirme la suppression en attente (un item ciblé, ou toutes les conversations). */
  async function handleConfirmDelete() {
    if (!pendingDelete) return;
    setDeleting(true);
    setDeleteError(null);

    try {
      if (pendingDelete === 'all') {
        await api.chat.deleteAllSessions();
        setSessions([]);
        // La conversation ouverte n'existe plus : retour à un état vierge.
        if (activeSessionId) navigate('/chat');
      } else {
        await api.chat.deleteSession(pendingDelete.id);
        setSessions(prev => prev.filter(s => s.id !== pendingDelete.id));
        if (pendingDelete.id === activeSessionId) navigate('/chat');
      }
      setPendingDelete(null);
    } catch (err) {
      setDeleteError((err as Error).message);
    } finally {
      setDeleting(false);
    }
  }

  return (
    <aside className="chat-sidebar">
      {/* Démarre une conversation vierge : l'URL /chat (sans id) réinitialise l'état de ChatPage. */}
      <button className="btn btn-primary chat-sidebar-new" onClick={() => navigate('/chat')}>
        + Nouvelle conversation
      </button>

      <ul className="chat-sidebar-list">
        {sessions.map(s => (
          <li key={s.id} className="chat-sidebar-row">
            <button
              className={`chat-sidebar-item ${s.id === activeSessionId ? 'chat-sidebar-item-active' : ''}`}
              onClick={() => navigate(`/chat/${s.id}`)}
              title={s.title}
            >
              <span className="chat-sidebar-item-title">{s.title || 'Sans titre'}</span>
              <span className="chat-sidebar-item-count">{s.messageCount}</span>
            </button>
            <button
              className="chat-sidebar-item-delete"
              onClick={e => { e.stopPropagation(); setPendingDelete({ id: s.id, title: s.title || 'Sans titre' }); }}
              title="Supprimer cette conversation"
              aria-label="Supprimer cette conversation"
            >
              🗑️
            </button>
          </li>
        ))}
        {sessions.length === 0 && (
          <li className="chat-sidebar-empty">Aucune conversation</li>
        )}
      </ul>

      {sessions.length > 0 && (
        <button className="chat-sidebar-clear-all" onClick={() => setPendingDelete('all')}>
          Tout supprimer
        </button>
      )}

      {/* Modale de confirmation — mêmes classes CSS que la suppression de documents (DocumentsPage). */}
      {pendingDelete && (
        <div className="modal-overlay" onClick={() => !deleting && setPendingDelete(null)}>
          <div className="modal" onClick={e => e.stopPropagation()}>
            <h2>Confirmer la suppression</h2>
            <p>
              {pendingDelete === 'all'
                ? `Voulez-vous vraiment supprimer les ${sessions.length} conversation(s) ?`
                : `Voulez-vous vraiment supprimer la conversation "${pendingDelete.title}" ?`}
            </p>
            <p className="modal-warning">Cette action est irréversible.</p>
            {deleteError && <p className="alert alert-error">{deleteError}</p>}
            <div className="modal-actions">
              <button className="btn btn-ghost" onClick={() => setPendingDelete(null)} disabled={deleting}>
                Annuler
              </button>
              <button className="btn btn-danger" onClick={handleConfirmDelete} disabled={deleting}>
                {deleting && <span className="btn-spinner" />}
                {deleting ? 'Suppression…' : 'Supprimer'}
              </button>
            </div>
          </div>
        </div>
      )}
    </aside>
  );
}
