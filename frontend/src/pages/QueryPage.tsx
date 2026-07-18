import { useState } from 'react';
import { runQuery } from '../api/client';
import type { QueryResponse } from '../api/types';

type QueryState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'done'; result: QueryResponse }
  | { status: 'error'; message: string };

export default function QueryPage() {
  const [query, setQuery] = useState('');
  const [state, setState] = useState<QueryState>({ status: 'idle' });

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!query.trim()) return;

    setState({ status: 'loading' });
    try {
      const result = await runQuery({ query });
      setState({ status: 'done', result });
    } catch (err) {
      setState({ status: 'error', message: err instanceof Error ? err.message : 'Error al consultar.' });
    }
  }

  return (
    <section>
      <h2>Buscar</h2>
      <p style={{ color: 'var(--text-muted)' }}>
        Preguntá en lenguaje natural sobre los documentos indexados.
      </p>

      <form onSubmit={handleSubmit} style={{ display: 'flex', gap: 8, marginTop: 16 }}>
        <input
          type="text"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder="¿Cuál es el plazo de entrega del contrato X?"
          style={{
            flex: 1,
            padding: '10px 12px',
            border: '1px solid var(--border)',
            borderRadius: 6,
            background: 'var(--bg)',
            color: 'var(--text)',
          }}
        />
        <button type="submit" disabled={state.status === 'loading'}>
          {state.status === 'loading' ? 'Buscando…' : 'Preguntar'}
        </button>
      </form>

      {state.status === 'error' && (
        <p style={{ color: 'var(--danger)', marginTop: 16 }}>{state.message}</p>
      )}

      {state.status === 'done' && (
        <div style={{ marginTop: 24 }}>
          <p style={{ whiteSpace: 'pre-wrap' }}>{state.result.answer}</p>

          {state.result.sources.length > 0 && (
            <div style={{ marginTop: 24 }}>
              <h3 style={{ fontSize: 16 }}>Fuentes</h3>
              <ul style={{ listStyle: 'none', padding: 0, display: 'flex', flexDirection: 'column', gap: 12 }}>
                {state.result.sources.map((source, i) => (
                  <li
                    key={`${source.docId}-${i}`}
                    style={{ border: '1px solid var(--border)', borderRadius: 8, padding: 12 }}
                  >
                    <div style={{ display: 'flex', justifyContent: 'space-between', color: 'var(--text-muted)', fontSize: 14 }}>
                      <span>
                        {source.filename} · página {source.page}
                      </span>
                      <span>score {source.score.toFixed(2)}</span>
                    </div>
                    <p style={{ margin: '8px 0 0' }}>{source.chunk}</p>
                  </li>
                ))}
              </ul>
            </div>
          )}
        </div>
      )}
    </section>
  );
}
