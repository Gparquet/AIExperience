import { useEffect, useRef, useState } from 'react';
import { flushSync } from 'react-dom';
import { api } from '../api/client';
import type { CitationResponse } from '../types';

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
  const [streamingContent, setStreamingContent] = useState('');
  const [error, setError] = useState<string | null>(null);
  const bottomRef = useRef<HTMLDivElement>(null);
  const streamingRef = useRef('');

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages, streamingContent]);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    const q = question.trim();
    if (!q || loading) return;

    setMessages(prev => [...prev, { role: 'user', content: q }]);
    setQuestion('');
    setLoading(true);
    setStreamingContent('');
    streamingRef.current = '';
    setError(null);

    try {
      for await (const event of api.chat.askStream({ question: q, documentIds: [] })) {
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
