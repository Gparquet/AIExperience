import type { AskQuestionRequest, AskQuestionResponse, DocumentResponse } from '../types';

const BASE_URL = import.meta.env.VITE_API_URL ?? '';

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE_URL}${path}`, init);
  if (!res.ok) {
    const text = await res.text().catch(() => res.statusText);
    throw new Error(text || `HTTP ${res.status}`);
  }
  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
}

export const api = {
  documents: {
    list: () => request<DocumentResponse[]>('/api/documents'),
    upload: (file: File, strategy = 'Recursive') => {
      const body = new FormData();
      body.append('file', file);
      return request<DocumentResponse>(`/api/documents?strategy=${strategy}`, {
        method: 'POST',
        body,
      });
    },
    delete: (id: string) => request<void>(`/api/documents/${id}`, { method: 'DELETE' }),
  },
  chat: {
    ask: (payload: AskQuestionRequest) =>
      request<AskQuestionResponse>('/api/chat/ask', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      }),
  },
};
