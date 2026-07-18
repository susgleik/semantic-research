import { NavLink, Route, Routes } from 'react-router-dom';
import UploadPage from './pages/UploadPage';
import DocumentsPage from './pages/DocumentsPage';
import QueryPage from './pages/QueryPage';
import ReportsPage from './pages/ReportsPage';

function navLinkStyle({ isActive }: { isActive: boolean }): React.CSSProperties {
  return {
    padding: '8px 4px',
    textDecoration: 'none',
    color: isActive ? 'var(--accent)' : 'var(--text-muted)',
    borderBottom: isActive ? '2px solid var(--accent)' : '2px solid transparent',
    fontWeight: isActive ? 600 : 500,
  };
}

export default function App() {
  return (
    <>
      <header style={{ paddingTop: 32, marginBottom: 32 }}>
        <h1 style={{ fontSize: 28, margin: '0 0 20px' }}>SemanticSearch</h1>
        <nav style={{ display: 'flex', gap: 24, borderBottom: '1px solid var(--border)' }}>
          <NavLink to="/" end style={navLinkStyle}>
            Subir documento
          </NavLink>
          <NavLink to="/documents" style={navLinkStyle}>
            Documentos
          </NavLink>
          <NavLink to="/query" style={navLinkStyle}>
            Buscar
          </NavLink>
          <NavLink to="/reports" style={navLinkStyle}>
            Informes
          </NavLink>
        </nav>
      </header>

      <main>
        <Routes>
          <Route path="/" element={<UploadPage />} />
          <Route path="/documents" element={<DocumentsPage />} />
          <Route path="/query" element={<QueryPage />} />
          <Route path="/reports" element={<ReportsPage />} />
        </Routes>
      </main>
    </>
  );
}
