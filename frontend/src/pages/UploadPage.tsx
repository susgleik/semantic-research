import { useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { createUpload, uploadFileToS3 } from '../api/client';
import { bannerStyle, cardStyle, fieldLabelStyle, fieldStyle } from '../styles';

const ALLOWED_EXTENSIONS = ['.pdf', '.docx'];

type UploadState =
  | { status: 'idle' }
  | { status: 'uploading'; filename: string }
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

    setState({ status: 'uploading', filename: file.name });
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

  const isUploading = state.status === 'uploading';

  return (
    <section>
      <h2>Subir documento</h2>
      <p style={{ color: 'var(--text-muted)' }}>
        Soporta PDF y Word (.docx). El documento se indexa automáticamente tras la subida.
      </p>

      <div style={{ ...cardStyle, marginTop: 16, display: 'flex', flexDirection: 'column', gap: 18, maxWidth: 560 }}>
        <label>
          <span style={fieldLabelStyle}>Categoría</span>
          <input
            type="text"
            value={category}
            onChange={(e) => setCategory(e.target.value)}
            style={{ ...fieldStyle, maxWidth: 260 }}
          />
        </label>

        <div
          onDragOver={(e) => {
            e.preventDefault();
            setIsDragging(true);
          }}
          onDragLeave={() => setIsDragging(false)}
          onDrop={onDrop}
          onClick={() => !isUploading && fileInputRef.current?.click()}
          style={{
            border: `2px dashed ${isDragging ? 'var(--accent)' : 'var(--border)'}`,
            borderRadius: 10,
            padding: '48px 24px',
            textAlign: 'center',
            cursor: isUploading ? 'default' : 'pointer',
            background: isDragging ? `color-mix(in srgb, var(--accent) 8%, var(--bg))` : 'var(--bg)',
            opacity: isUploading ? 0.6 : 1,
            transition: 'background 0.15s, border-color 0.15s',
          }}
        >
          <div style={{ fontSize: 32, marginBottom: 8 }}>📤</div>
          <p style={{ margin: 0, fontWeight: 600 }}>
            {isUploading ? `Subiendo "${state.filename}"…` : 'Arrastrá un archivo aquí o hacé clic para elegirlo'}
          </p>
          <p style={{ margin: '6px 0 0', color: 'var(--text-muted)', fontSize: 13 }}>
            {ALLOWED_EXTENSIONS.join(' · ')}
          </p>
          <input
            ref={fileInputRef}
            type="file"
            accept={ALLOWED_EXTENSIONS.join(',')}
            onChange={onFileInputChange}
            disabled={isUploading}
            style={{ display: 'none' }}
          />
        </div>

        {state.status === 'done' && (
          <div style={bannerStyle('var(--success)')}>
            <strong>"{state.filename}"</strong> subido correctamente. La indexación puede tardar unos segundos —
            revisá el estado en{' '}
            <Link to="/documents" style={{ color: 'inherit', textDecoration: 'underline' }}>
              Documentos
            </Link>
            .
            <div style={{ marginTop: 6, fontSize: 12, opacity: 0.85, fontFamily: 'ui-monospace, monospace' }}>
              docId: {state.docId}
            </div>
          </div>
        )}

        {state.status === 'error' && <div style={bannerStyle('var(--danger)')}>{state.message}</div>}
      </div>
    </section>
  );
}
