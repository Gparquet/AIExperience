import { useEffect, useRef, useState } from 'react';
import { flushSync } from 'react-dom';
import { useLocation } from 'react-router-dom';
import { api } from '../api/client';
import type { CitationResponse, DocumentResponse } from '../types';

interface Message {
  role: 'user' | 'assistant';
  content: string;
  meta?: { strategy: string; tokens: number; duration: number };
  citations?: CitationResponse[];
}

// State optionnel transmis par DocumentsPage ou VideoPage via navigate('/chat', { state })
interface ChatLocationState {
  documentId?: string;
  documentName?: string;
}

export default function ChatPage() {
  const location = useLocation();
  const locationState = (location.state as ChatLocationState | null) ?? {};

  // Filtre actif : documentId ciblé (null = recherche globale sur tous les documents)
  const [filteredDocumentId, setFilteredDocumentId] = useState<string | null>(
    locationState.documentId ?? null
  );
  const [filteredDocumentName, setFilteredDocumentName] = useState<string | null>(
    locationState.documentName ?? null
  );

  // Liste des documents disponibles pour le sélecteur
  const [documents, setDocuments] = useState<DocumentResponse[]>([]);

  const [messages, setMessages] = useState<Message[]>([]);
  const [question, setQuestion] = useState('');
  const [loading, setLoading] = useState(false);
  const [streamingContent, setStreamingContent] = useState('');
  const [error, setError] = useState<string | null>(null);
  const bottomRef = useRef<HTMLDivElement>(null);
  const streamingRef = useRef('');

  // Chargement de la liste des documents pour le sélecteur
  useEffect(() => {
    api.documents.list()
      .then(docs => setDocuments(docs.filter(d => d.status === 'Completed')))
      .catch(() => { /* sélecteur non bloquant */ });
  }, []);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages, streamingContent]);

  function handleDocumentSelect(e: React.ChangeEvent<HTMLSelectElement>) {
    const val = e.target.value;
    if (!val) {
      setFilteredDocumentId(null);
      setFilteredDocumentName(null);
    } else {
      const doc = documents.find(d => d.id === val);
      setFilteredDocumentId(val);
      setFilteredDocumentName(doc?.fileName ?? null);
    }
  }

  async function handleSubmit(e: { preventDefault(): void }) {
    e.preventDefault();
    const q = question.trim();
    if (!q || loading) return;

    setMessages(prev => [...prev, { role: 'user', content: q }]);
    setQuestion('');
    setLoading(true);
    setStreamingContent('');
    streamingRef.current = '';
    setError(null);

    // Filtre : tableau avec l'id ciblé, ou vide pour recherche globale
    const documentIds = filteredDocumentId ? [filteredDocumentId] : [];

    try {
      for await (const event of api.chat.askStream({ question: q, documentIds })) {
        if (event.event === 'token') {
          streamingRef.current += event.data.token;
          flushSync(() => {
            setStreamingContent(streamingRef.current);
          });
        } else if (event.event === 'done') {
          const res = event.data;
          setStreamingContent('');
          streamingRef.current = '';
          setMessages(prev => [...prev, {
            role: 'assistant',
            content: res.answer,
            meta: { strategy: res.strategyUsed, tokens: res.totalTokens, duration: res.durationMs },
            citations: res.citations,
          }]);
        } else if (event.event === 'error') {
          throw new Error(event.data.message);
        }
      }
    } catch (err) {
      setError((err as Error).message);
      setStreamingContent('');
      streamingRef.current = '';
      setMessages(prev => prev.slice(0, -1));
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="chat-main">
      {/* Sélecteur de document — permet de cibler un document ou de rechercher globalement */}
      <div className="chat-filter-bar">
        <label className="chat-filter-label" htmlFor="doc-select">
          Rechercher dans :
        </label>
        <select
          id="doc-select"
          className="chat-filter-select"
          value={filteredDocumentId ?? ''}
          onChange={handleDocumentSelect}
          disabled={loading}
        >
          <option value="">Tous les documents</option>
          {documents.map(doc => (
            <option key={doc.id} value={doc.id}>{doc.fileName}</option>
          ))}
        </select>
        {filteredDocumentId && (
          <button
            className="btn btn-ghost btn-sm"
            onClick={() => { setFilteredDocumentId(null); setFilteredDocumentName(null); }}
            disabled={loading}
          >
            ✕ Retirer le filtre
          </button>
        )}
      </div>

      {/* Badge indiquant le filtre actif */}
      {filteredDocumentName && (
        <div className="chat-filter-badge">
          Filtré sur : <strong>{filteredDocumentName}</strong>
        </div>
      )}

      <div className="messages">
        {messages.length === 0 && (
          <div className="empty-state">
            <span>💬</span>
            <p>
              {filteredDocumentId
                ? `Posez une question sur "${filteredDocumentName}".`
                : 'Posez une question sur vos documents.'}
            </p>
          </div>
        )}
        {messages.map((msg, i) => (
          <div key={i} className={`message message-${msg.role}`}>
            <div className="message-bubble">
              <p>{msg.content}</p>
              {msg.citations && msg.citations.length > 0 && (
                <details className="citations">
                  <summary>{msg.citations.length} source(s)</summary>
                  <ul>
                    {msg.citations.map((c, j) => (
                      <li key={j}>
                        <strong>{c.documentName}</strong>
                        {c.pageNumber != null && ` · p.${c.pageNumber}`}
                        {' · '}
                        <em>{c.excerpt.slice(0, 120)}{c.excerpt.length > 120 ? '…' : ''}</em>
                      </li>
                    ))}
                  </ul>
                </details>
              )}
              {msg.meta && (
                <div className="message-meta">
                  {msg.meta.strategy} · {msg.meta.tokens} tokens · {msg.meta.duration} ms
                </div>
              )}
            </div>
          </div>
        ))}
        {loading && (
          <div className="message message-assistant">
            <div className="message-bubble">
              {streamingContent
                ? <p>{streamingContent}</p>
                : <div className="typing"><span /><span /><span /></div>
              }
            </div>
          </div>
        )}
        <div ref={bottomRef} />
      </div>

      {error && <div className="alert alert-error">{error}</div>}

      <form className="chat-input-row" onSubmit={handleSubmit}>
        <input
          type="text"
          placeholder="Posez votre question…"
          value={question}
          onChange={e => setQuestion(e.target.value)}
          disabled={loading}
          autoFocus
        />
        <button type="submit" className="btn btn-primary" disabled={loading || !question.trim()}>
          Envoyer
        </button>
      </form>
    </div>
  );
}
