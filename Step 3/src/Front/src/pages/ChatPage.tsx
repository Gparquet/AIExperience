import { useEffect, useRef, useState } from 'react';
import { flushSync } from 'react-dom';
import { useLocation } from 'react-router-dom';
import { api } from '../api/client';
import type { CitationResponse, DocumentResponse } from '../types';

/** Les 3 modes de démonstration disponibles dans l'interface. */
type Mode = 'classic' | 'llm' | 'rag';


interface Message {
  role: 'user' | 'assistant';
  content: string;
  meta?: { strategy: string; tokens: number; duration: number };
  citations?: CitationResponse[];
  /** Mode utilisé pour générer cette réponse — détermine le rendu visuel. */
  mode?: Mode;
}

// State optionnel transmis par DocumentsPage ou VideoPage via navigate('/chat', { state })
interface ChatLocationState {
  documentId?: string;
  documentName?: string;
}

const MODE_LABELS: Record<Mode, string> = {
  classic: '🔍 Recherche classique',
  llm: '🤖 LLM direct',
  rag: '✨ RAG + LLM',
};

const MODE_PLACEHOLDER: Record<Mode, string> = {
  classic: 'Recherchez un mot-clé…',
  llm: 'Posez votre question…',
  rag: 'Posez votre question…',
};

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
  const [mode, setMode] = useState<Mode>('rag');
  const bottomRef = useRef<HTMLDivElement>(null);
  const streamingRef = useRef('');

  // Prompts système éditables par mode — chargés depuis le back-end (source de vérité unique)
  const [systemPrompts, setSystemPrompts] = useState<Record<'llm' | 'rag', string>>({ llm: '', rag: '' });
  const [defaultSystemPrompts, setDefaultSystemPrompts] = useState<Record<'llm' | 'rag', string>>({ llm: '', rag: '' });
  const [showSystemPrompt, setShowSystemPrompt] = useState(false);

  // Chargement de la liste des documents pour le sélecteur
  useEffect(() => {
    api.documents.list()
      .then(docs => setDocuments(docs.filter(d => d.status === 'Completed')))
      .catch(() => { /* sélecteur non bloquant */ });
  }, []);

  // Chargement des prompts système par défaut depuis le back-end (source de vérité unique)
  useEffect(() => {
    api.chat.getSystemPrompts()
      .then(prompts => {
        const defaults = { rag: prompts.rag, llm: prompts.directLlm };
        setDefaultSystemPrompts(defaults);
        setSystemPrompts(defaults);
      })
      .catch(() => { /* panneau reste vide si l'API est indisponible */ });
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

    const useLlm = mode !== 'classic';
    const useRag = mode === 'rag';
    const currentMode = mode;

    // Filtre : tableau avec l'id ciblé, ou vide pour recherche globale
    const documentIds = filteredDocumentId ? [filteredDocumentId] : [];

    // Prompt système : envoyé uniquement pour les modes LLM (pas pour la recherche full-text)
    const systemPrompt = useLlm ? systemPrompts[mode as 'llm' | 'rag'] : undefined;

    try {
      for await (const event of api.chat.askStream({ question: q, documentIds, useLlm, useRag, systemPrompt })) {
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
            mode: currentMode,
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
      {/* Barre de mode : 3 boutons de démonstration */}
      <div className="mode-bar">
        <button
          className={`mode-btn ${mode === 'classic' ? 'mode-btn-active' : ''}`}
          onClick={() => setMode('classic')}
          disabled={loading}
          title="Recherche full-text PostgreSQL — mots-clés exacts, aucun LLM, aucun embedding"
        >
          {MODE_LABELS.classic}
        </button>
        <div className="mode-divider" />
        <button
          className={`mode-btn mode-btn-llm ${mode === 'llm' ? 'mode-btn-active' : ''}`}
          onClick={() => setMode('llm')}
          disabled={loading}
          title="Question posée directement au LLM, sans consulter vos documents"
        >
          {MODE_LABELS.llm}
        </button>
        <div className="mode-divider" />
        <button
          className={`mode-btn mode-btn-rag ${mode === 'rag' ? 'mode-btn-active' : ''}`}
          onClick={() => setMode('rag')}
          disabled={loading}
          title="Recherche sémantique par embeddings + synthèse LLM avec citations"
        >
          {MODE_LABELS.rag}
        </button>
      </div>

      {/* Bouton d'accès à l'éditeur de prompt système */}
      {mode !== 'classic' && (
        <div className="system-prompt-toggle">
          <button
            className={`btn btn-ghost btn-sm system-prompt-btn ${showSystemPrompt ? 'system-prompt-btn-active' : ''}`}
            onClick={() => setShowSystemPrompt(v => !v)}
            disabled={loading}
            title="Modifier le prompt système envoyé au LLM"
          >
            ⚙️ Prompt système {showSystemPrompt ? '▲' : '▼'}
          </button>
        </div>
      )}

      {/* Panneau d'édition du prompt système — visible uniquement pour les modes LLM */}
      {showSystemPrompt && mode !== 'classic' && (
        <div className="system-prompt-panel">
          <div className="system-prompt-header">
            <span className="system-prompt-label">
              Prompt système — mode {mode === 'rag' ? 'RAG + LLM' : 'LLM direct'}
            </span>
            <button
              className="btn btn-ghost btn-xs"
              onClick={() => setSystemPrompts(prev => ({
                ...prev,
                [mode]: defaultSystemPrompts[mode as 'llm' | 'rag'],
              }))}
              disabled={loading}
              title="Remettre le prompt par défaut"
            >
              ↺ Réinitialiser
            </button>
          </div>
          <textarea
            className="system-prompt-textarea"
            value={systemPrompts[mode as 'llm' | 'rag']}
            onChange={e => setSystemPrompts(prev => ({ ...prev, [mode]: e.target.value }))}
            disabled={loading}
            rows={6}
            spellCheck={false}
          />
        </div>
      )}

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
            <span>{mode === 'rag' ? '✨' : mode === 'llm' ? '🤖' : '🔍'}</span>
            <p>
              {mode === 'classic'
                ? 'Posez une question — les résultats les plus proches textuellement seront affichés.'
                : mode === 'llm'
                  ? 'Posez une question — le LLM répond depuis ses connaissances générales, sans vos documents.'
                  : filteredDocumentId
                    ? `Posez une question sur "${filteredDocumentName}".`
                    : 'Posez une question — le LLM synthétisera une réponse à partir de vos documents.'}
            </p>
          </div>
        )}

        {messages.map((msg, i) => (
          <div key={i} className={`message message-${msg.role}`}>
            {msg.role === 'user' ? (
              <div className="message-bubble">
                <p>{msg.content}</p>
              </div>
            ) : msg.mode === 'classic' ? (
              /* Affichage "moteur de recherche" — résultats full-text bruts */
              <div className="search-results">
                <div className="search-results-header">
                  <span className="search-icon">🔍</span>
                  <span>{msg.content}</span>
                </div>
                {msg.citations && msg.citations.length > 0 ? (
                  <ul className="search-result-list">
                    {msg.citations.map((c, j) => (
                      <li key={j} className="search-result-card">
                        <div className="search-result-top">
                          <span className="search-result-doc">{c.documentName}</span>
                          {c.pageNumber != null && (
                            <span className="search-result-page">p.{c.pageNumber}</span>
                          )}
                          <span className="search-result-score">
                            {(c.score * 100).toFixed(1)} pts
                          </span>
                        </div>
                        <p className="search-result-excerpt">{c.excerpt}</p>
                      </li>
                    ))}
                  </ul>
                ) : null}
                {msg.meta && (
                  <div className="message-meta search-result-meta">
                    {msg.meta.strategy} · {msg.meta.duration} ms
                  </div>
                )}
              </div>
            ) : msg.mode === 'llm' ? (
              /* Affichage "LLM direct" — bulle sans citations, badge distinctif */
              <div className="message-bubble message-bubble-llm">
                <div className="llm-badge">🤖 Sans documents</div>
                <p>{msg.content}</p>
                {msg.meta && (
                  <div className="message-meta">
                    {msg.meta.strategy} · {msg.meta.tokens} tokens · {msg.meta.duration} ms
                  </div>
                )}
              </div>
            ) : (
              /* Affichage "RAG + LLM" — bulle avec citations enrichies */
              <div className="message-bubble">
                <p>{msg.content}</p>
                {msg.citations && msg.citations.length > 0 && (
                  <details className="citations">
                    <summary>{msg.citations.length} source(s)</summary>
                    <ul>
                      {msg.citations.map((c, j) => (
                        <li key={j}>
                          <div className="citation-header">
                            <strong>{c.documentName}</strong>
                            {c.pageNumber != null && (
                              <span className="search-result-page">p.{c.pageNumber}</span>
                            )}
                            <span className="search-result-score">
                              {(c.score * 100).toFixed(1)} %
                            </span>
                          </div>
                          {c.sectionTitle && (
                            <div className="citation-section">{c.sectionTitle}</div>
                          )}
                          <em className="citation-excerpt">{c.excerpt}</em>
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
            )}
          </div>
        ))}

        {loading && (
          <div className="message message-assistant">
            <div className={mode === 'classic' ? 'search-results' : mode === 'llm' ? 'message-bubble message-bubble-llm' : 'message-bubble'}>
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

      <form
        className={`chat-input-row ${mode === 'classic' ? 'chat-input-row-classic' : mode === 'llm' ? 'chat-input-row-llm' : ''}`}
        onSubmit={handleSubmit}
      >
        <input
          type="text"
          placeholder={MODE_PLACEHOLDER[mode]}
          value={question}
          onChange={e => setQuestion(e.target.value)}
          disabled={loading}
          autoFocus
        />
        <button
          type="submit"
          className={`btn ${mode === 'classic' ? 'btn-search' : mode === 'llm' ? 'btn-llm' : 'btn-primary'}`}
          disabled={loading || !question.trim()}
        >
          {mode === 'classic' ? 'Rechercher' : 'Envoyer'}
        </button>
      </form>
    </div>
  );
}
