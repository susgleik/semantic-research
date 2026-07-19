import { useEffect, useState } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { createReport, getReport } from '../api/client';
import type { ReportResponse, ReportScenario } from '../api/types';
import { cardStyle, fieldLabelStyle, fieldStyle, primaryButtonStyle, secondaryButtonStyle } from '../styles';

type ReportState =
  | { status: 'idle' }
  | { status: 'generating' }
  | { status: 'ready'; report: ReportResponse }
  | { status: 'error'; message: string };

type PreviewState =
  | { status: 'loading' }
  | { status: 'ready'; markdown: string }
  | { status: 'error' };

interface HistoryEntry {
  reportId: string;
  scenario: ReportScenario;
  createdAt: string;
}

const HISTORY_KEY = 'semantic-search:reports-history';
const HISTORY_LIMIT = 10;

const SCENARIOS: { value: ReportScenario; icon: string; label: string; description: string }[] = [
  { value: 'summary', icon: '📄', label: 'Resumen ejecutivo', description: 'Resumen del corpus completo (o filtrado).' },
  { value: 'risks', icon: '⚠️', label: 'Riesgos e inconsistencias', description: 'Detecta riesgos o contradicciones entre documentos.' },
  { value: 'compare', icon: '🔍', label: 'Comparar dos documentos', description: 'Requiere elegir exactamente 2 documentos.' },
  { value: 'extract', icon: '🧾', label: 'Extraer datos clave', description: 'Fechas, nombres, montos y cláusulas relevantes.' },
  { value: 'custom', icon: '✏️', label: 'Instrucción personalizada', description: 'Escribí libremente qué querés analizar.' },
];

const scenarioLabel = (value: ReportScenario): string => SCENARIOS.find((s) => s.value === value)?.label ?? value;

function loadHistory(): HistoryEntry[] {
  try {
    const raw = localStorage.getItem(HISTORY_KEY);
    return raw ? (JSON.parse(raw) as HistoryEntry[]) : [];
  } catch {
    return [];
  }
}

function relativeTime(iso: string): string {
  const diffMs = Date.now() - new Date(iso).getTime();
  const minutes = Math.round(diffMs / 60000);
  if (minutes < 1) return 'ahora mismo';
  if (minutes < 60) return `hace ${minutes} min`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `hace ${hours} h`;
  const days = Math.round(hours / 24);
  return `hace ${days} d`;
}

export default function ReportsPage() {
  const [scenario, setScenario] = useState<ReportScenario>('summary');
  const [category, setCategory] = useState('');
  const [documentIdsRaw, setDocumentIdsRaw] = useState('');
  const [instruction, setInstruction] = useState('');
  const [dateFrom, setDateFrom] = useState('');
  const [dateTo, setDateTo] = useState('');
  const [state, setState] = useState<ReportState>({ status: 'idle' });
  const [preview, setPreview] = useState<PreviewState>({ status: 'loading' });
  const [history, setHistory] = useState<HistoryEntry[]>(() => loadHistory());

  useEffect(() => {
    localStorage.setItem(HISTORY_KEY, JSON.stringify(history));
  }, [history]);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();

    const documentIds = documentIdsRaw
      .split(',')
      .map((id) => id.trim())
      .filter(Boolean);

    setState({ status: 'generating' });
    try {
      const report = await createReport({
        scenario,
        category: category.trim() || undefined,
        documentIds: documentIds.length > 0 ? documentIds : undefined,
        instruction: instruction.trim() || undefined,
        dateFrom: dateFrom || undefined,
        dateTo: dateTo || undefined,
      });
      setState({ status: 'ready', report });
      setHistory((prev) =>
        [{ reportId: report.reportId, scenario, createdAt: new Date().toISOString() }, ...prev].slice(
          0,
          HISTORY_LIMIT,
        ),
      );
    } catch (err) {
      setState({ status: 'error', message: err instanceof Error ? err.message : 'No se pudo generar el informe.' });
    }
  }

  async function handleViewHistoryEntry(entry: HistoryEntry) {
    setState({ status: 'generating' });
    try {
      const report = await getReport(entry.reportId);
      setState({ status: 'ready', report });
    } catch (err) {
      setState({
        status: 'error',
        message: err instanceof Error ? err.message : 'No se pudo recuperar ese informe (puede haber expirado).',
      });
    }
  }

  function handleClearHistory() {
    if (!confirm('¿Borrar el historial de informes generados en este navegador?')) return;
    setHistory([]);
  }

  useEffect(() => {
    if (state.status !== 'ready' || !state.report.downloadUrl) return;

    const downloadUrl = state.report.downloadUrl;
    setPreview({ status: 'loading' });

    fetch(downloadUrl)
      .then((res) => {
        if (!res.ok) throw new Error(`No se pudo cargar el informe (status ${res.status}).`);
        return res.text();
      })
      .then((markdown) => setPreview({ status: 'ready', markdown }))
      .catch(() => setPreview({ status: 'error' }));
  }, [state]);

  function handleDownloadMarkdown(reportId: string, markdown: string) {
    const blob = new Blob([markdown], { type: 'text/markdown' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `informe-${reportId}.md`;
    link.click();
    URL.revokeObjectURL(url);
  }

  function handleDownloadPdf(markdown: string) {
    const html = renderToStaticMarkup(
      <ReactMarkdown remarkPlugins={[remarkGfm]}>{markdown}</ReactMarkdown>,
    );

    const printWindow = window.open('', '_blank');
    if (!printWindow) return;

    printWindow.document.write(`<!doctype html>
<html>
<head>
<meta charset="utf-8" />
<title>Informe SemanticSearch</title>
<style>
  body { font: 15px/1.6 system-ui, 'Segoe UI', Roboto, sans-serif; color: #1b1a1f; max-width: 720px; margin: 32px auto; padding: 0 24px; }
  h1, h2, h3 { letter-spacing: -0.02em; }
  code { background: #f6f5f8; border-radius: 4px; padding: 1px 5px; }
  pre { background: #f6f5f8; border-radius: 8px; padding: 12px; overflow-x: auto; }
  table { border-collapse: collapse; width: 100%; margin: 12px 0; }
  th, td { border: 1px solid #e5e4e7; padding: 6px 10px; text-align: left; }
  blockquote { margin: 8px 0; padding: 4px 16px; border-left: 3px solid #7c3aed; color: #6b6375; }
</style>
</head>
<body>${html}</body>
</html>`);
    printWindow.document.close();
    printWindow.focus();
    setTimeout(() => printWindow.print(), 300);
  }

  return (
    <section>
      <h2>Informes</h2>
      <p style={{ color: 'var(--text-muted)' }}>
        Generá un análisis del corpus completo (o filtrado) en vez de una pregunta puntual.
      </p>

      <div style={{ display: 'flex', gap: 24, marginTop: 16, alignItems: 'flex-start', flexWrap: 'wrap' }}>
        <form onSubmit={handleSubmit} style={{ ...cardStyle, flex: '2 1 420px', display: 'flex', flexDirection: 'column', gap: 18 }}>
          <div>
            <span style={fieldLabelStyle}>Escenario</span>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(180px, 1fr))', gap: 8, marginTop: 8 }}>
              {SCENARIOS.map((s) => {
                const isSelected = s.value === scenario;
                return (
                  <button
                    type="button"
                    key={s.value}
                    onClick={() => setScenario(s.value)}
                    style={{
                      textAlign: 'left',
                      padding: 12,
                      borderRadius: 10,
                      border: isSelected ? '1.5px solid var(--accent)' : '1px solid var(--border)',
                      background: isSelected ? 'color-mix(in srgb, var(--accent) 12%, var(--bg))' : 'var(--bg)',
                      color: 'var(--text)',
                      cursor: 'pointer',
                    }}
                  >
                    <div style={{ fontSize: 20, marginBottom: 4 }}>{s.icon}</div>
                    <div style={{ fontWeight: 600, fontSize: 13 }}>{s.label}</div>
                    <div style={{ color: 'var(--text-muted)', fontSize: 12, marginTop: 2 }}>{s.description}</div>
                  </button>
                );
              })}
            </div>
          </div>

          <label>
            <span style={fieldLabelStyle}>Categoría (opcional)</span>
            <input type="text" value={category} onChange={(e) => setCategory(e.target.value)} style={fieldStyle} />
          </label>

          {scenario === 'compare' && (
            <label>
              <span style={fieldLabelStyle}>IDs de los 2 documentos a comparar (separados por coma)</span>
              <input
                type="text"
                value={documentIdsRaw}
                onChange={(e) => setDocumentIdsRaw(e.target.value)}
                placeholder="doc-id-1, doc-id-2"
                style={fieldStyle}
              />
            </label>
          )}

          {scenario !== 'compare' && (
            <label>
              <span style={fieldLabelStyle}>IDs de documentos (opcional, separados por coma)</span>
              <input type="text" value={documentIdsRaw} onChange={(e) => setDocumentIdsRaw(e.target.value)} style={fieldStyle} />
            </label>
          )}

          {scenario === 'custom' && (
            <label>
              <span style={fieldLabelStyle}>Instrucción</span>
              <textarea value={instruction} onChange={(e) => setInstruction(e.target.value)} rows={3} style={fieldStyle} />
            </label>
          )}

          <div style={{ display: 'flex', gap: 12 }}>
            <label style={{ flex: 1 }}>
              <span style={fieldLabelStyle}>Desde (opcional)</span>
              <input type="date" value={dateFrom} onChange={(e) => setDateFrom(e.target.value)} style={fieldStyle} />
            </label>
            <label style={{ flex: 1 }}>
              <span style={fieldLabelStyle}>Hasta (opcional)</span>
              <input type="date" value={dateTo} onChange={(e) => setDateTo(e.target.value)} style={fieldStyle} />
            </label>
          </div>

          <button type="submit" disabled={state.status === 'generating'} style={primaryButtonStyle}>
            {state.status === 'generating' ? 'Generando…' : 'Generar informe'}
          </button>
        </form>

        <aside style={{ ...cardStyle, flex: '1 1 260px' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <h3 style={{ margin: 0, fontSize: 16 }}>Historial</h3>
            {history.length > 0 && (
              <button
                onClick={handleClearHistory}
                style={{ fontSize: 12, color: 'var(--text-muted)', background: 'none', border: 'none' }}
              >
                Borrar
              </button>
            )}
          </div>

          {history.length === 0 && (
            <p style={{ color: 'var(--text-muted)', fontSize: 13, marginTop: 8 }}>
              Todavía no generaste informes en este navegador.
            </p>
          )}

          <ul style={{ listStyle: 'none', padding: 0, margin: '12px 0 0', display: 'flex', flexDirection: 'column', gap: 8 }}>
            {history.map((entry) => (
              <li key={`${entry.reportId}-${entry.createdAt}`}>
                <button
                  onClick={() => handleViewHistoryEntry(entry)}
                  disabled={state.status === 'generating'}
                  style={{ ...secondaryButtonStyle, width: '100%', textAlign: 'left', display: 'block' }}
                >
                  <div style={{ fontSize: 13, fontWeight: 600 }}>{scenarioLabel(entry.scenario)}</div>
                  <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 2 }}>
                    {relativeTime(entry.createdAt)}
                  </div>
                </button>
              </li>
            ))}
          </ul>
        </aside>
      </div>

      {state.status === 'error' && (
        <p style={{ color: 'var(--danger)', marginTop: 16 }}>{state.message}</p>
      )}

      {state.status === 'ready' && state.report.downloadUrl && (
        <div style={{ marginTop: 24 }}>
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12 }}>
            <p style={{ color: 'var(--success)', margin: 0 }}>Informe listo.</p>
            {preview.status === 'ready' && (
              <div style={{ display: 'flex', gap: 8 }}>
                <button
                  onClick={() => handleDownloadMarkdown(state.report.reportId, preview.markdown)}
                  style={secondaryButtonStyle}
                >
                  Descargar .md
                </button>
                <button onClick={() => handleDownloadPdf(preview.markdown)} style={secondaryButtonStyle}>
                  Descargar PDF
                </button>
              </div>
            )}
          </div>

          {preview.status === 'loading' && (
            <p style={{ color: 'var(--text-muted)', marginTop: 12 }}>Cargando informe…</p>
          )}

          {preview.status === 'error' && (
            <p style={{ color: 'var(--danger)', marginTop: 12 }}>
              No se pudo cargar la vista previa. Podés{' '}
              <a href={state.report.downloadUrl} target="_blank" rel="noreferrer">
                abrir el archivo directo
              </a>
              .
            </p>
          )}

          {preview.status === 'ready' && (
            <div className="report-content" style={{ ...cardStyle, marginTop: 16 }}>
              <ReactMarkdown remarkPlugins={[remarkGfm]}>{preview.markdown}</ReactMarkdown>
            </div>
          )}
        </div>
      )}
    </section>
  );
}
