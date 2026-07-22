/** Statuts possibles d'un document dans le pipeline d'ingestion. */
export type DocumentStatus = 'Pending' | 'Processing' | 'Completed' | 'Failed';

export interface DocumentResponse {
  id: string;
  fileName: string;
  contentType: string;
  fileSizeBytes: number;
  status: DocumentStatus;
  createdAt: string;
  /** Raison de l'échec si le statut est "Failed", absente sinon. */
  errorMessage?: string | null;
  /** Avancement fin d'ingestion (présent surtout pendant/juste après le traitement). */
  ingestionProgress?: IngestionProgress | null;
}

/** Événement poussé par le hub SignalR à chaque changement de statut d'un document. */
export interface DocumentStatusChangedEvent {
  documentId: string;
  status: DocumentStatus;
  errorMessage: string | null;
}

/** Étapes fines du pipeline d'ingestion (miroir de l'enum back-end IngestionStage). */
export type IngestionStage =
  | 'Queued' | 'ExtractingAudio' | 'Transcribing' | 'ExtractingText'
  | 'Chunking' | 'Embedding' | 'Storing' | 'Completed' | 'Failed';

/** Compteurs de détail d'une étape (tous optionnels selon l'étape). */
export interface IngestionProgressCounters {
  batchIndex?: number | null;
  batchCount?: number | null;
  chunksDone?: number | null;
  chunksTotal?: number | null;
  segmentsDone?: number | null;
  segmentsTotal?: number | null;
}

/** Instantané d'avancement fin d'une ingestion. */
export interface IngestionProgress {
  stage: IngestionStage;
  percent: number | null;
  counters: IngestionProgressCounters;
  updatedAt: string;
}

/** Événement SignalR d'avancement fin, distinct de DocumentStatusChangedEvent. */
export interface DocumentProgressChangedEvent {
  documentId: string;
  stage: IngestionStage;
  percent: number | null;
  counters: IngestionProgressCounters;
}

/** Type de correspondance détecté avec un document existant de même nom. */
export type DocumentMatchType = 'ExactDuplicate' | 'SameNameDifferentContent';

/** Informations minimales sur un document existant en conflit (détection de doublon ou de nouvelle version). */
export interface ExistingDocumentInfo {
  id: string;
  fileName: string;
  createdAt: string;
}

/** Réponse de la pré-vérification de doublon avant upload. */
export interface CheckDuplicateResponse {
  isDuplicate: boolean;
  existingDocument: ExistingDocumentInfo | null;
  matchType: DocumentMatchType | null;
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
  /** Session de conversation à poursuivre. Absent = nouvelle conversation. */
  sessionId?: string;
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
  /** Session à laquelle appartient cet échange (nouvelle ou réutilisée). */
  sessionId: string;
}

/** Résumé d'une conversation pour la liste latérale. */
export interface ChatSessionSummary {
  id: string;
  title: string;
  updatedAt: string;
  messageCount: number;
}

/** Un message d'une conversation rechargée depuis l'historique. */
export interface ChatMessageDetail {
  role: string;
  content: string;
  citations?: CitationResponse[] | null;
  strategyUsed?: string | null;
  tokensUsed: number;
  durationMs: number;
  createdAt: string;
}

/** Détail complet d'une conversation. */
export interface ChatSessionDetail {
  id: string;
  title: string;
  messages: ChatMessageDetail[];
}

export type StreamEvent =
  | { event: 'token'; data: { token: string } }
  | { event: 'done'; data: AskQuestionResponse }
  | { event: 'error'; data: { message: string } };

/**
 * Transcription d'un document vidéo/audio, récupérée après coup une fois le document "Completed"
 * (la transcription elle-même n'est plus renvoyée en synchrone par l'upload, qui est maintenant
 * traité en arrière-plan).
 */
export interface VideoTranscriptionResponse {
  rawTranscription: string | null;
  cleanedTranscription: string | null;
}
