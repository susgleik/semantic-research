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

// "sub" del usuario logueado — se manda como x-amz-meta-owner-id al subir un archivo
// a S3, para que indexer-service pueda estampar el owner del documento.
let ownerId: string | null = null;

export function setOwnerId(sub: string | null): void {
  ownerId = sub;
}

export function getOwnerId(): string | null {
  return ownerId;
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  // "Content-Type: application/json" es un header no-simple para el navegador: dispara
  // preflight OPTIONS en cualquier request, incluso los GET sin body. sam local start-api
  // no puede responder ese preflight cuando la API usa el recurso HttpApi implícito (bug
  // conocido de SAM CLI, no hay fix de template — https://github.com/aws/aws-sam-cli/issues/3803),
  // así que en modo A (backend local) toda request rompía con 403 "Missing Authentication
  // Token" antes siquiera de llegar al Lambda. Los Lambdas nunca validan este header (solo
  // hacen JsonSerializer.Deserialize(request.Body)), así que evitamos el preflight:
  // "text/plain" es uno de los 3 valores CORS-safelisted que el navegador no preflightea,
  // y no se manda ningún Content-Type si no hay body. En AWS real (Modo B, con JWT real)
  // el Authorization header igual fuerza preflight — ahí no hace falta este workaround
  // porque API Gateway HTTP API sí resuelve el preflight a nivel de gateway, sin Lambda.
  const headers: Record<string, string> = {};
  if (init?.body) headers['Content-Type'] = 'text/plain;charset=UTF-8';
  if (authToken) headers['Authorization'] = `Bearer ${authToken}`;

  const response = await fetch(`${API_URL}${path}`, {
    ...init,
    headers: { ...headers, ...init?.headers },
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

export async function uploadFileToS3(
  uploadUrl: string,
  file: File,
  contentType: string,
  ownerHeader?: string,
): Promise<void> {
  const headers: Record<string, string> = { 'Content-Type': contentType };
  // Debe ser byte-idéntico al ownerId que firmó la URL prefirmada en /upload, o S3
  // rechaza el PUT con SignatureDoesNotMatch.
  if (ownerHeader) headers['x-amz-meta-owner-id'] = ownerHeader;

  const response = await fetch(uploadUrl, {
    method: 'PUT',
    headers,
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
