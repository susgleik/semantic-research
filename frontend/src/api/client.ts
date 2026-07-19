import type {
  ApiErrorBody,
  DocumentListResponse,
  QueryRequest,
  QueryResponse,
  ReportRequest,
  ReportResponse,
  UploadRequest,
  UploadResponse,
} from './types';

const API_URL = import.meta.env.VITE_API_URL ?? 'http://localhost:3000';

let authToken: string | null = null;

export function setAuthToken(token: string | null): void {
  authToken = token;
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_URL}${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...(authToken ? { Authorization: `Bearer ${authToken}` } : {}),
      ...init?.headers,
    },
  });

  if (!response.ok) {
    const body = (await response.json().catch(() => null)) as ApiErrorBody | null;
    throw new Error(body?.error ?? `Error ${response.status} llamando a ${path}`);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

export function createUpload(req: UploadRequest): Promise<UploadResponse> {
  return request<UploadResponse>('/upload', {
    method: 'POST',
    body: JSON.stringify(req),
  });
}

export async function uploadFileToS3(uploadUrl: string, file: File, contentType: string): Promise<void> {
  const response = await fetch(uploadUrl, {
    method: 'PUT',
    headers: { 'Content-Type': contentType },
    body: file,
  });

  if (!response.ok) {
    throw new Error(`No se pudo subir el archivo a S3 (status ${response.status}).`);
  }
}

export function listDocuments(limit = 20, offset = 0): Promise<DocumentListResponse> {
  return request<DocumentListResponse>(`/documents?limit=${limit}&offset=${offset}`);
}

export function reindexDocument(docId: string): Promise<void> {
  return request<void>(`/reindex/${encodeURIComponent(docId)}`, { method: 'POST' });
}

export function deleteDocument(docId: string): Promise<void> {
  return request<void>(`/documents/${encodeURIComponent(docId)}`, { method: 'DELETE' });
}

export function runQuery(req: QueryRequest): Promise<QueryResponse> {
  return request<QueryResponse>('/query', {
    method: 'POST',
    body: JSON.stringify(req),
  });
}

export function createReport(req: ReportRequest): Promise<ReportResponse> {
  return request<ReportResponse>('/reports', {
    method: 'POST',
    body: JSON.stringify(req),
  });
}

export function getReport(reportId: string): Promise<ReportResponse> {
  return request<ReportResponse>(`/reports/${encodeURIComponent(reportId)}`);
}
