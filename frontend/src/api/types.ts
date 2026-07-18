export interface UploadRequest {
  filename: string;
  category: string;
  contentType: string;
}

export interface UploadResponse {
  docId: string;
  filename: string;
  status: string;
  uploadUrl: string;
}

export interface DocumentSummary {
  docId: string;
  filename: string;
  category: string;
  status: string;
  chunkCount: number;
  indexedAt: string;
}

export interface DocumentListResponse {
  documents: DocumentSummary[];
  total: number;
  limit: number;
  offset: number;
}

export interface QueryRequest {
  query: string;
  topK?: number;
}

export interface SourceChunk {
  docId: string;
  filename: string;
  chunk: string;
  score: number;
  page: number;
}

export interface QueryResponse {
  answer: string;
  sources: SourceChunk[];
}

export type ReportScenario = 'summary' | 'risks' | 'compare' | 'extract' | 'custom';

export interface ReportRequest {
  scenario: ReportScenario;
  category?: string;
  documentIds?: string[];
  instruction?: string;
  dateFrom?: string;
  dateTo?: string;
}

export interface ReportResponse {
  reportId: string;
  status: string;
  downloadUrl?: string;
}

export interface ApiErrorBody {
  error: string;
}
