import type { AskQuestionRequest, AskQuestionResponse, CheckDuplicateResponse, DocumentResponse, StreamEvent, SystemPromptsResponse, TranscribeVideoResponse } from '../types';

const BASE_URL = import.meta.env.VITE_API_URL ?? '';

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE_URL}${path}`, init);
  if (!res.ok) {
    const text = await res.text().catch(() => res.statusText);
    // On attache le code HTTP à l'erreur pour permettre à l'appelant de distinguer
    // un 409 (doublon détecté côté back-end) d'une autre erreur et d'afficher
    // un message générique plutôt que le corps JSON brut de la réponse.
    const error = new Error(text || `HTTP ${res.status}`) as Error & { status?: number };
    error.status = res.status;
    throw error;
  }
  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
}

export const api = {
  documents: {
    list: () => request<DocumentResponse[]>('/api/documents'),
    // Pré-vérification légère avant upload : hash calculé côté navigateur, pas d'envoi du fichier complet.
    checkDuplicate: (fileName: string, contentHash: string) =>
      request<CheckDuplicateResponse>('/api/documents/check-duplicate', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ fileName, contentHash }),
      }),
    upload: (file: File, strategy = 'Recursive', replaceDocumentId?: string) => {
      const body = new FormData();
      body.append('file', file);
      const params = new URLSearchParams({ strategy });
      if (replaceDocumentId) params.set('replaceDocumentId', replaceDocumentId);
      return request<DocumentResponse>(`/api/documents?${params}`, {
        method: 'POST',
        body,
      });
    },
    delete: (id: string) => request<void>(`/api/documents/${id}`, { method: 'DELETE' }),
  },
  video: {
    transcribe: (
      file: File,
      language = 'fr',
      cleanWithLlm = true,
      autoIngest = true,
      title?: string,
    ) => {
      const body = new FormData();
      body.append('file', file);
      const params = new URLSearchParams({
        language,
        cleanWithLlm: String(cleanWithLlm),
        autoIngest: String(autoIngest),
        ...(title ? { title } : {}),
      });
      return request<TranscribeVideoResponse>(`/api/video/transcribe?${params}`, {
        method: 'POST',
        body,
      });
    },
  },
  chat: {
    getSystemPrompts: () => request<SystemPromptsResponse>('/api/chat/system-prompts'),

    ask: (payload: AskQuestionRequest) =>
      request<AskQuestionResponse>('/api/chat/ask', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      }),

    async *askStream(payload: AskQuestionRequest): AsyncGenerator<StreamEvent> {
      const res = await fetch(`${BASE_URL}/api/chat/stream`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      });

      if (!res.ok || !res.body) {
        const text = await res.text().catch(() => res.statusText);
        throw new Error(text || `HTTP ${res.status}`);
      }

      const reader = res.body.getReader();
      const decoder = new TextDecoder();
      let buffer = '';

      try {
        while (true) {
          const { done, value } = await reader.read();
          if (done) break;

          buffer += decoder.decode(value, { stream: true });
          const parts = buffer.split('\n\n');
          buffer = parts.pop() ?? '';

          for (const block of parts) {
            let eventName = 'message';
            let data = '';
            for (const line of block.split('\n')) {
              if (line.startsWith('event: ')) eventName = line.slice(7);
              else if (line.startsWith('data: ')) data = line.slice(6);
            }
            if (data) yield { event: eventName, data: JSON.parse(data) } as StreamEvent;
          }
        }
      } finally {
        reader.releaseLock();
      }
    },
  },
};
