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
}

export interface AskQuestionRequest {
  question: string;
  documentIds: string[];
  strategy?: string;
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
