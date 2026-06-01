import { useEffect, useRef, useState } from 'react';
import { api } from '../api/client';
import type { AskQuestionResponse, CitationResponse } from '../types';

interface Message {
  role: 'user' | 'assistant';
  content: string;
  meta?: { strategy: string; tokens: number; duration: number };
  citations?: CitationResponse[];
}

export default function ChatPage() {
  const [messages, setMessages] = useState<Message[]>([]);
  const [question, setQuestion] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const bottomRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages]);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    const q = question.trim();
    if (!q || loading) return;

    setMessages(prev => [...prev, { role: 'user', content: q }]);
    setQuestion('');
    setLoading(true);
    setError(null);

    try {
      const res: AskQuestionResponse = await api.chat.ask({
        question: q,
        documentIds: [],
      });
      setMessages(prev => [...prev, {
        role: 'assistant',
        content: res.answer,
        meta: { strategy: res.strategyUsed, tokens: res.totalTokens, duration: res.durationMs },
        citations: res.citations,
      }]);
    } catch (err) {
      setError((err as Error).message);
      setMessages(prev => prev.slice(0, -1));
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="chat-main">
      <div className="messages">
        {messages.length === 0 && (
          <div className="empty-state">
            <span>💬</span>
            <p>Posez une question sur vos documents.</p>
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
            <div className="message-bubble typing">
              <span /><span /><span />
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
