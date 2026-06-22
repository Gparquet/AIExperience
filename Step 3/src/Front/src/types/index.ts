export interface DocumentResponse {
  id: string;
  fileName: string;
  contentType: string;
  fileSizeBytes: number;
  status: string;
  createdAt: string;
}

export interface CitationResponse {
  documentName: string;
  pageNumber: number | null;
  excerpt: string;
  score: number;
  /** Titre de la section du document source (null si non renseigné). */
  sectionTitle?: string | null;
  /** Position ordinale du chunk dans le document (0-based). */
  chunkIndex?: number;
}

export interface AskQuestionRequest {
  question: string;
  documentIds: string[];
  strategy?: string;
  /** Quand false : recherche full-text PostgreSQL sans LLM (mode démonstration). */
  useLlm?: boolean;
  /** Quand false et useLlm=true : question envoyée directement au LLM sans récupération documentaire. */
  useRag?: boolean;
  /** Prompt système personnalisé. Si absent, le back-end utilise le prompt par défaut. */
  systemPrompt?: string;
}

export interface SystemPromptsResponse {
  rag: string;
  directLlm: string;
}

export interface AskQuestionResponse {
  answer: string;
  citations: CitationResponse[];
  strategyUsed: string;
  totalTokens: number;
  durationMs: number;
}

export type StreamEvent =
  | { event: 'token'; data: { token: string } }
  | { event: 'done'; data: AskQuestionResponse }
  | { event: 'error'; data: { message: string } };

export interface TranscribeVideoResponse {
  /** Transcription brute avec timestamps [hh:mm:ss → hh:mm:ss] */
  rawTranscription: string;
  /** Transcription nettoyée par le LLM (si demandée) */
  cleanedTranscription: string | null;
  /** Durée totale de la vidéo/audio (ex: "00:05:42.1234") */
  duration: string;
  /** Nombre de segments transcrits */
  segmentCount: number;
  /** ID du document créé dans le RAG (si autoIngest = true) */
  documentId: string | null;
  /** Temps de traitement total (ex: "00:01:23.4567") */
  processingTime: string;
}
