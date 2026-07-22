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

export default function ChatSidebar({ activeSessionId, reloadSignal }: ChatSidebarProps) {
  const navigate = useNavigate();
  const [sessions, setSessions] = useState<ChatSessionSummary[]>([]);

  // Rechargement de la liste au montage puis à chaque nouvel échange (reloadSignal).
  useEffect(() => {
    api.chat.listSessions()
      .then(setSessions)
      .catch(() => { /* la sidebar reste vide si l'API est indisponible — non bloquant */ });
  }, [reloadSignal]);

  return (
    <aside className="chat-sidebar">
      {/* Démarre une conversation vierge : l'URL /chat (sans id) réinitialise l'état de ChatPage. */}
      <button className="btn btn-primary chat-sidebar-new" onClick={() => navigate('/chat')}>
        + Nouvelle conversation
      </button>

      <ul className="chat-sidebar-list">
        {sessions.map(s => (
          <li key={s.id}>
            <button
              className={`chat-sidebar-item ${s.id === activeSessionId ? 'chat-sidebar-item-active' : ''}`}
              onClick={() => navigate(`/chat/${s.id}`)}
              title={s.title}
            >
              <span className="chat-sidebar-item-title">{s.title || 'Sans titre'}</span>
              <span className="chat-sidebar-item-count">{s.messageCount}</span>
            </button>
          </li>
        ))}
        {sessions.length === 0 && (
          <li className="chat-sidebar-empty">Aucune conversation</li>
        )}
      </ul>
    </aside>
  );
}
