import { useState } from 'react';
import { createReport } from '../api/client';
import type { ReportResponse, ReportScenario } from '../api/types';

type ReportState =
  | { status: 'idle' }
  | { status: 'generating' }
  | { status: 'ready'; report: ReportResponse }
  | { status: 'error'; message: string };

const SCENARIOS: { value: ReportScenario; label: string; description: string }[] = [
  { value: 'summary', label: 'Resumen ejecutivo', description: 'Resumen del corpus completo (o filtrado).' },
  { value: 'risks', label: 'Riesgos e inconsistencias', description: 'Detecta riesgos o contradicciones entre documentos.' },
  { value: 'compare', label: 'Comparar dos documentos', description: 'Requiere elegir exactamente 2 documentos.' },
  { value: 'extract', label: 'Extraer datos clave', description: 'Fechas, nombres, montos y cláusulas relevantes.' },
  { value: 'custom', label: 'Instrucción personalizada', description: 'Escribí libremente qué querés analizar.' },
];

export default function ReportsPage() {
  const [scenario, setScenario] = useState<ReportScenario>('summary');
  const [category, setCategory] = useState('');
  const [documentIdsRaw, setDocumentIdsRaw] = useState('');
  const [instruction, setInstruction] = useState('');
  const [dateFrom, setDateFrom] = useState('');
  const [dateTo, setDateTo] = useState('');
  const [state, setState] = useState<ReportState>({ status: 'idle' });

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
    } catch (err) {
      setState({ status: 'error', message: err instanceof Error ? err.message : 'No se pudo generar el informe.' });
    }
  }

  const selected = SCENARIOS.find((s) => s.value === scenario)!;

  return (
    <section>
      <h2>Informes</h2>
      <p style={{ color: 'var(--text-muted)' }}>
        Generá un análisis del corpus completo (o filtrado) en vez de una pregunta puntual.
      </p>

      <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 16, marginTop: 16, maxWidth: 480 }}>
        <label>
          Escenario
          <select
            value={scenario}
            onChange={(e) => setScenario(e.target.value as ReportScenario)}
            style={{ display: 'block', marginTop: 4, padding: '8px 10px', width: '100%' }}
          >
            {SCENARIOS.map((s) => (
              <option key={s.value} value={s.value}>
                {s.label}
              </option>
            ))}
          </select>
          <span style={{ color: 'var(--text-muted)', fontSize: 14 }}>{selected.description}</span>
        </label>

        <label>
          Categoría (opcional)
          <input
            type="text"
            value={category}
            onChange={(e) => setCategory(e.target.value)}
            style={{ display: 'block', marginTop: 4, padding: '8px 10px', width: '100%' }}
          />
        </label>

        {scenario === 'compare' && (
          <label>
            IDs de los 2 documentos a comparar (separados por coma)
            <input
              type="text"
              value={documentIdsRaw}
              onChange={(e) => setDocumentIdsRaw(e.target.value)}
              placeholder="doc-id-1, doc-id-2"
              style={{ display: 'block', marginTop: 4, padding: '8px 10px', width: '100%' }}
            />
          </label>
        )}

        {scenario !== 'compare' && (
          <label>
            IDs de documentos (opcional, separados por coma)
            <input
              type="text"
              value={documentIdsRaw}
              onChange={(e) => setDocumentIdsRaw(e.target.value)}
              style={{ display: 'block', marginTop: 4, padding: '8px 10px', width: '100%' }}
            />
          </label>
        )}

        {scenario === 'custom' && (
          <label>
            Instrucción
            <textarea
              value={instruction}
              onChange={(e) => setInstruction(e.target.value)}
              rows={3}
              style={{ display: 'block', marginTop: 4, padding: '8px 10px', width: '100%' }}
            />
          </label>
        )}

        <div style={{ display: 'flex', gap: 12 }}>
          <label style={{ flex: 1 }}>
            Desde (opcional)
            <input
              type="date"
              value={dateFrom}
              onChange={(e) => setDateFrom(e.target.value)}
              style={{ display: 'block', marginTop: 4, padding: '8px 10px', width: '100%' }}
            />
          </label>
          <label style={{ flex: 1 }}>
            Hasta (opcional)
            <input
              type="date"
              value={dateTo}
              onChange={(e) => setDateTo(e.target.value)}
              style={{ display: 'block', marginTop: 4, padding: '8px 10px', width: '100%' }}
            />
          </label>
        </div>

        <button type="submit" disabled={state.status === 'generating'}>
          {state.status === 'generating' ? 'Generando…' : 'Generar informe'}
        </button>
      </form>

      {state.status === 'error' && (
        <p style={{ color: 'var(--danger)', marginTop: 16 }}>{state.message}</p>
      )}

      {state.status === 'ready' && state.report.downloadUrl && (
        <p style={{ marginTop: 16, color: 'var(--success)' }}>
          Informe listo.{' '}
          <a href={state.report.downloadUrl} target="_blank" rel="noreferrer">
            Descargar
          </a>
        </p>
      )}
    </section>
  );
}
