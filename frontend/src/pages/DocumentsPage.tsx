import { useEffect, useState } from 'react';
import { deleteDocument, listDocuments, reindexDocument } from '../api/client';
import type { DocumentSummary } from '../api/types';

const PAGE_SIZE = 20;

const statusColor: Record<string, string> = {
  indexed: 'var(--success)',
  failed: 'var(--danger)',
};

export default function DocumentsPage() {
  const [documents, setDocuments] = useState<DocumentSummary[]>([]);
  const [total, setTotal] = useState(0);
  const [offset, setOffset] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [pendingDocId, setPendingDocId] = useState<string | null>(null);

  async function load() {
    setLoading(true);
    setError(null);
    try {
      const response = await listDocuments(PAGE_SIZE, offset);
      setDocuments(response.documents);
      setTotal(response.total);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'No se pudo cargar la lista de documentos.');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [offset]);

  async function handleReindex(docId: string) {
    setPendingDocId(docId);
    try {
      await reindexDocument(docId);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'No se pudo reindexar el documento.');
    } finally {
      setPendingDocId(null);
    }
  }

  async function handleDelete(docId: string) {
    if (!confirm('¿Eliminar este documento y todos sus chunks indexados?')) return;

    setPendingDocId(docId);
    try {
      await deleteDocument(docId);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'No se pudo eliminar el documento.');
    } finally {
      setPendingDocId(null);
    }
  }

  return (
    <section>
      <h2>Documentos</h2>

      {error && <p style={{ color: 'var(--danger)' }}>{error}</p>}
      {loading && <p style={{ color: 'var(--text-muted)' }}>Cargando…</p>}

      {!loading && documents.length === 0 && (
        <p style={{ color: 'var(--text-muted)' }}>Todavía no hay documentos indexados.</p>
      )}

      {documents.length > 0 && (
        <table style={{ width: '100%', borderCollapse: 'collapse', marginTop: 16 }}>
          <thead>
            <tr style={{ textAlign: 'left', borderBottom: '1px solid var(--border)' }}>
              <th style={{ padding: '8px 4px' }}>Archivo</th>
              <th style={{ padding: '8px 4px' }}>Categoría</th>
              <th style={{ padding: '8px 4px' }}>Estado</th>
              <th style={{ padding: '8px 4px' }}>Chunks</th>
              <th style={{ padding: '8px 4px' }}>Indexado</th>
              <th style={{ padding: '8px 4px' }}></th>
            </tr>
          </thead>
          <tbody>
            {documents.map((doc) => (
              <tr key={doc.docId} style={{ borderBottom: '1px solid var(--border)' }}>
                <td style={{ padding: '8px 4px' }}>{doc.filename}</td>
                <td style={{ padding: '8px 4px' }}>{doc.category}</td>
                <td style={{ padding: '8px 4px', color: statusColor[doc.status] ?? 'var(--text)' }}>
                  {doc.status}
                </td>
                <td style={{ padding: '8px 4px' }}>{doc.chunkCount}</td>
                <td style={{ padding: '8px 4px', color: 'var(--text-muted)' }}>
                  {new Date(doc.indexedAt).toLocaleString()}
                </td>
                <td style={{ padding: '8px 4px', display: 'flex', gap: 8 }}>
                  <button disabled={pendingDocId === doc.docId} onClick={() => handleReindex(doc.docId)}>
                    Reindexar
                  </button>
                  <button
                    disabled={pendingDocId === doc.docId}
                    onClick={() => handleDelete(doc.docId)}
                    style={{ color: 'var(--danger)' }}
                  >
                    Eliminar
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {total > PAGE_SIZE && (
        <div style={{ marginTop: 16, display: 'flex', gap: 12, alignItems: 'center' }}>
          <button disabled={offset === 0} onClick={() => setOffset(Math.max(0, offset - PAGE_SIZE))}>
            Anterior
          </button>
          <span style={{ color: 'var(--text-muted)' }}>
            {offset + 1}–{Math.min(offset + PAGE_SIZE, total)} de {total}
          </span>
          <button disabled={offset + PAGE_SIZE >= total} onClick={() => setOffset(offset + PAGE_SIZE)}>
            Siguiente
          </button>
        </div>
      )}
    </section>
  );
}
