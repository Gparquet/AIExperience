import type { AskQuestionRequest, AskQuestionResponse, CheckDuplicateResponse, DocumentResponse, StreamEvent, SystemPromptsResponse, VideoTranscriptionResponse } from '../types';

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

/**
 * Envoie un formulaire multipart via XMLHttpRequest plutôt que fetch, pour pouvoir suivre la
 * progression réelle du transfert (fetch ne l'expose pas côté requête). Traite tout code 2xx
 * (y compris 202 Accepted, utilisé par les uploads désormais traités en arrière-plan) comme un
 * succès, symétriquement à request().
 */
function uploadWithProgress<T>(
  path: string,
  formData: FormData,
  onProgress?: (percent: number) => void,
): Promise<T> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open('POST', `${BASE_URL}${path}`);

    xhr.upload.onprogress = event => {
      if (onProgress && event.lengthComputable) {
        onProgress(Math.round((event.loaded / event.total) * 100));
      }
    };

    xhr.onload = () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        resolve(xhr.responseText ? (JSON.parse(xhr.responseText) as T) : (undefined as T));
        return;
      }
      const error = new Error(xhr.responseText || `HTTP ${xhr.status}`) as Error & { status?: number };
      error.status = xhr.status;
      reject(error);
    };

    xhr.onerror = () => reject(new Error("Échec réseau pendant l'envoi du fichier."));

    xhr.send(formData);
  });
}

export const api = {
  documents: {
    list: () => request<DocumentResponse[]>('/api/documents'),
    get: (id: string) => request<DocumentResponse>(`/api/documents/${id}`),
    // Pré-vérification légère avant upload : hash calculé côté navigateur, pas d'envoi du fichier complet.
    checkDuplicate: (fileName: string, contentHash: string) =>
      request<CheckDuplicateResponse>('/api/documents/check-duplicate', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ fileName, contentHash }),
      }),
    // 202 Accepted : le document est créé (statut "Pending"), l'ingestion elle-même se déroule
    // en arrière-plan — le statut final arrive via SignalR (ou le polling de repli).
    upload: (file: File, strategy = 'Recursive', replaceDocumentId?: string, onProgress?: (percent: number) => void) => {
      const body = new FormData();
      body.append('file', file);
      const params = new URLSearchParams({ strategy });
      if (replaceDocumentId) params.set('replaceDocumentId', replaceDocumentId);
      return uploadWithProgress<DocumentResponse>(`/api/documents?${params}`, body, onProgress);
    },
    delete: (id: string) => request<void>(`/api/documents/${id}`, { method: 'DELETE' }),
  },
  video: {
    // 202 Accepted, comme documents.upload — la transcription elle-même est récupérée après coup
    // via video.getTranscription() une fois le document "Completed".
    transcribe: (
      file: File,
      language = 'fr',
      cleanWithLlm = true,
      autoIngest = true,
      title?: string,
      onProgress?: (percent: number) => void,
    ) => {
      const body = new FormData();
      body.append('file', file);
      const params = new URLSearchParams({
        language,
        cleanWithLlm: String(cleanWithLlm),
        autoIngest: String(autoIngest),
        ...(title ? { title } : {}),
      });
      return uploadWithProgress<DocumentResponse>(`/api/video/transcribe?${params}`, body, onProgress);
    },
    getTranscription: (id: string) => request<VideoTranscriptionResponse>(`/api/video/${id}/transcription`),
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
