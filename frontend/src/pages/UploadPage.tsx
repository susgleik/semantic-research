import { useRef, useState } from 'react';
import { createUpload, uploadFileToS3 } from '../api/client';

const ALLOWED_EXTENSIONS = ['.pdf', '.docx'];

type UploadState =
  | { status: 'idle' }
  | { status: 'uploading' }
  | { status: 'done'; docId: string; filename: string }
  | { status: 'error'; message: string };

function guessContentType(filename: string): string {
  if (filename.toLowerCase().endsWith('.pdf')) return 'application/pdf';
  if (filename.toLowerCase().endsWith('.docx')) {
    return 'application/vnd.openxmlformats-officedocument.wordprocessingml.document';
  }
  return 'application/octet-stream';
}

export default function UploadPage() {
  const [category, setCategory] = useState('general');
  const [isDragging, setIsDragging] = useState(false);
  const [state, setState] = useState<UploadState>({ status: 'idle' });
  const fileInputRef = useRef<HTMLInputElement>(null);

  async function handleFile(file: File) {
    const extension = file.name.slice(file.name.lastIndexOf('.')).toLowerCase();
    if (!ALLOWED_EXTENSIONS.includes(extension)) {
      setState({ status: 'error', message: `Tipo de archivo no soportado. Permitidos: ${ALLOWED_EXTENSIONS.join(', ')}` });
      return;
    }

    setState({ status: 'uploading' });
    try {
      const contentType = guessContentType(file.name);
      const { docId, uploadUrl } = await createUpload({
        filename: file.name,
        category,
        contentType,
      });

      await uploadFileToS3(uploadUrl, file, contentType);

      setState({ status: 'done', docId, filename: file.name });
    } catch (err) {
      setState({ status: 'error', message: err instanceof Error ? err.message : 'Error desconocido al subir el archivo.' });
    }
  }

  function onDrop(e: React.DragEvent<HTMLDivElement>) {
    e.preventDefault();
    setIsDragging(false);
    const file = e.dataTransfer.files[0];
    if (file) void handleFile(file);
  }

  function onFileInputChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (file) void handleFile(file);
    e.target.value = '';
  }

  return (
    <section>
      <h2>Subir documento</h2>
      <p style={{ color: 'var(--text-muted)' }}>
        Soporta PDF y Word (.docx). El documento se indexa automáticamente tras la subida.
      </p>

      <label style={{ display: 'block', margin: '16px 0' }}>
        Categoría
        <input
          type="text"
          value={category}
          onChange={(e) => setCategory(e.target.value)}
          style={{
            display: 'block',
            marginTop: 4,
            padding: '8px 10px',
            border: '1px solid var(--border)',
            borderRadius: 6,
            background: 'var(--bg)',
            color: 'var(--text)',
            width: 240,
          }}
        />
      </label>

      <div
        onDragOver={(e) => {
          e.preventDefault();
          setIsDragging(true);
        }}
        onDragLeave={() => setIsDragging(false)}
        onDrop={onDrop}
        onClick={() => fileInputRef.current?.click()}
        style={{
          border: `2px dashed ${isDragging ? 'var(--accent)' : 'var(--border)'}`,
          borderRadius: 10,
          padding: '48px 24px',
          textAlign: 'center',
          cursor: 'pointer',
          background: isDragging ? 'var(--bg-subtle)' : 'transparent',
        }}
      >
        <p style={{ margin: 0, color: 'var(--text-muted)' }}>
          Arrastrá un archivo aquí o hacé clic para elegirlo
        </p>
        <input
          ref={fileInputRef}
          type="file"
          accept={ALLOWED_EXTENSIONS.join(',')}
          onChange={onFileInputChange}
          style={{ display: 'none' }}
        />
      </div>

      <div style={{ marginTop: 20 }}>
        {state.status === 'uploading' && <p>Subiendo…</p>}
        {state.status === 'done' && (
          <p style={{ color: 'var(--success)' }}>
            "{state.filename}" subido correctamente (docId: {state.docId}). La indexación puede tardar unos segundos.
          </p>
        )}
        {state.status === 'error' && <p style={{ color: 'var(--danger)' }}>{state.message}</p>}
      </div>
    </section>
  );
}
