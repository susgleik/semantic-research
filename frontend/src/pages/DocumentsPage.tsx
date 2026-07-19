import { useEffect, useState } from 'react';
import { deleteDocument, listDocuments, reindexDocument } from '../api/client';
import type { DocumentSummary } from '../api/types';
import { badgeStyle, cardStyle, dangerButtonStyle, secondaryButtonStyle } from '../styles';

const PAGE_SIZE = 20;

const statusMeta: Record<string, { color: string; label: string }> = {
  indexed: { color: 'var(--success)', label: 'Indexado' },
  failed: { color: 'var(--danger)', label: 'Falló' },
};

export default function DocumentsPage() {
  const [documents, setDocuments] = useState<DocumentSummary[]>([]);
  const [total, setTotal] = useState(0);
  const [offset, setOffset] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [pendingDocId, setPendingDocId] = useState<string | null>(null);
  const [copiedDocId, setCopiedDocId] = useState<string | null>(null);

  async function handleCopyId(docId: string) {
    await navigator.clipboard.writeText(docId);
    setCopiedDocId(docId);
    setTimeout(() => setCopiedDocId((current) => (current === docId ? null : current)), 1500);
  }

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
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', gap: 12 }}>
        <h2 style={{ margin: 0 }}>Documentos</h2>
        {total > 0 && <span style={{ color: 'var(--text-muted)', fontSize: 14 }}>{total} en total</span>}
      </div>

      {error && (
        <div style={{ ...badgeStyle('var(--danger)'), display: 'block', marginTop: 12, borderRadius: 8 }}>{error}</div>
      )}

      {loading && <p style={{ color: 'var(--text-muted)', marginTop: 16 }}>Cargando…</p>}

      {!loading && documents.length === 0 && !error && (
        <div style={{ ...cardStyle, marginTop: 16, textAlign: 'center', color: 'var(--text-muted)' }}>
          Todavía no hay documentos indexados. Subí uno desde "Subir documento".
        </div>
      )}

      {documents.length > 0 && (
        <div style={{ ...cardStyle, marginTop: 16, padding: 0, overflow: 'hidden' }}>
          <table style={{ width: '100%', borderCollapse: 'collapse' }}>
            <thead>
              <tr style={{ textAlign: 'left', borderBottom: '1px solid var(--border)', background: 'var(--bg)' }}>
                <th style={{ padding: '12px 16px', fontSize: 13, color: 'var(--text-muted)' }}>Archivo</th>
                <th style={{ padding: '12px 16px', fontSize: 13, color: 'var(--text-muted)' }}>ID</th>
                <th style={{ padding: '12px 16px', fontSize: 13, color: 'var(--text-muted)' }}>Categoría</th>
                <th style={{ padding: '12px 16px', fontSize: 13, color: 'var(--text-muted)' }}>Estado</th>
                <th style={{ padding: '12px 16px', fontSize: 13, color: 'var(--text-muted)' }}>Chunks</th>
                <th style={{ padding: '12px 16px', fontSize: 13, color: 'var(--text-muted)' }}>Indexado</th>
                <th style={{ padding: '12px 16px' }}></th>
              </tr>
            </thead>
            <tbody>
              {documents.map((doc, i) => {
                const meta = statusMeta[doc.status] ?? { color: 'var(--text-muted)', label: doc.status };
                return (
                  <tr
                    key={doc.docId}
                    style={{ borderBottom: i < documents.length - 1 ? '1px solid var(--border)' : 'none' }}
                  >
                    <td style={{ padding: '12px 16px', fontWeight: 500 }}>{doc.filename}</td>
                    <td style={{ padding: '12px 16px' }}>
                      <button
                        onClick={() => handleCopyId(doc.docId)}
                        title={doc.docId}
                        style={{
                          font: 'inherit',
                          fontFamily: 'ui-monospace, SFMono-Regular, Consolas, monospace',
                          fontSize: 12,
                          color: 'var(--text-muted)',
                          background: 'var(--bg)',
                          border: '1px solid var(--border)',
                          borderRadius: 6,
                          padding: '3px 6px',
                        }}
                      >
                        {copiedDocId === doc.docId ? 'Copiado ✓' : `${doc.docId.slice(0, 8)}…`}
                      </button>
                    </td>
                    <td style={{ padding: '12px 16px', color: 'var(--text-muted)' }}>{doc.category}</td>
                    <td style={{ padding: '12px 16px' }}>
                      <span style={badgeStyle(meta.color)}>{meta.label}</span>
                    </td>
                    <td style={{ padding: '12px 16px', color: 'var(--text-muted)' }}>{doc.chunkCount}</td>
                    <td style={{ padding: '12px 16px', color: 'var(--text-muted)', fontSize: 13 }}>
                      {new Date(doc.indexedAt).toLocaleString()}
                    </td>
                    <td style={{ padding: '12px 16px', display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
                      <button
                        disabled={pendingDocId === doc.docId}
                        onClick={() => handleReindex(doc.docId)}
                        style={secondaryButtonStyle}
                      >
                        Reindexar
                      </button>
                      <button
                        disabled={pendingDocId === doc.docId}
                        onClick={() => handleDelete(doc.docId)}
                        style={dangerButtonStyle}
                      >
                        Eliminar
                      </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      {total > PAGE_SIZE && (
        <div style={{ marginTop: 16, display: 'flex', gap: 12, alignItems: 'center' }}>
          <button disabled={offset === 0} onClick={() => setOffset(Math.max(0, offset - PAGE_SIZE))} style={secondaryButtonStyle}>
            Anterior
          </button>
          <span style={{ color: 'var(--text-muted)', fontSize: 14 }}>
            {offset + 1}–{Math.min(offset + PAGE_SIZE, total)} de {total}
          </span>
          <button
            disabled={offset + PAGE_SIZE >= total}
            onClick={() => setOffset(offset + PAGE_SIZE)}
            style={secondaryButtonStyle}
          >
            Siguiente
          </button>
        </div>
      )}
    </section>
  );
}
