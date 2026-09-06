import { useEffect } from 'react';
import { NavLink, Route, Routes } from 'react-router-dom';
import { useAuth } from 'react-oidc-context';
import UploadPage from './pages/UploadPage';
import DocumentsPage from './pages/DocumentsPage';
import QueryPage from './pages/QueryPage';
import ReportsPage from './pages/ReportsPage';
import { authEnabled, cognitoLogoutUrl } from './auth/config';
import { setAuthToken, setOwnerId } from './api/client';

function navLinkStyle({ isActive }: { isActive: boolean }): React.CSSProperties {
  return {
    padding: '8px 4px',
    textDecoration: 'none',
    color: isActive ? 'var(--accent)' : 'var(--text-muted)',
    borderBottom: isActive ? '2px solid var(--accent)' : '2px solid transparent',
    fontWeight: isActive ? 600 : 500,
  };
}

function AppRoutes() {
  return (
    <main>
      <Routes>
        <Route path="/" element={<UploadPage />} />
        <Route path="/documents" element={<DocumentsPage />} />
        <Route path="/query" element={<QueryPage />} />
        <Route path="/reports" element={<ReportsPage />} />
      </Routes>
    </main>
  );
}

function AppShell({ userEmail, onLogout }: { userEmail?: string; onLogout?: () => void }) {
  return (
    <>
      <header style={{ paddingTop: 32, marginBottom: 32 }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end', gap: 16 }}>
          <h1 style={{ fontSize: 28, margin: '0 0 20px' }}>SemanticSearch</h1>
          {userEmail && (
            <div style={{ display: 'flex', gap: 12, alignItems: 'center', marginBottom: 20 }}>
              <span style={{ color: 'var(--text-muted)', fontSize: 14 }}>{userEmail}</span>
              <button
                onClick={onLogout}
                style={{
                  padding: '6px 14px',
                  borderRadius: 8,
                  border: '1px solid var(--border)',
                  background: 'transparent',
                  color: 'var(--text)',
                }}
              >
                Cerrar sesión
              </button>
            </div>
          )}
        </div>
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

      <AppRoutes />
    </>
  );
}

function LoginScreen() {
  const auth = useAuth();

  useEffect(() => {
    setAuthToken(null);
  }, []);

  return (
    <div
      style={{
        minHeight: '80vh',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
      }}
    >
      <div
        style={{
          width: '100%',
          maxWidth: 380,
          padding: 40,
          borderRadius: 16,
          border: '1px solid var(--border)',
          background: 'var(--bg-subtle)',
          boxShadow: '0 8px 30px rgba(0, 0, 0, 0.08)',
          textAlign: 'center',
        }}
      >
        <div
          style={{
            width: 56,
            height: 56,
            margin: '0 auto 20px',
            borderRadius: 14,
            background: 'var(--accent)',
            color: 'var(--accent-contrast)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            fontSize: 24,
            fontWeight: 700,
          }}
        >
          S
        </div>

        <h1 style={{ fontSize: 22, margin: '0 0 8px' }}>SemanticSearch</h1>
        <p style={{ color: 'var(--text-muted)', margin: '0 0 28px', fontSize: 14, lineHeight: 1.5 }}>
          Iniciá sesión con tu cuenta para subir documentos, hacer búsquedas semánticas y
          generar informes.
        </p>

        {auth.isLoading && (
          <p style={{ color: 'var(--text-muted)', fontSize: 14 }}>Conectando con Cognito…</p>
        )}

        {auth.error && (
          <p
            style={{
              color: 'var(--danger)',
              fontSize: 13,
              background: 'color-mix(in srgb, var(--danger) 12%, transparent)',
              border: '1px solid var(--danger)',
              borderRadius: 8,
              padding: '10px 12px',
              marginBottom: 20,
              textAlign: 'left',
            }}
          >
            No se pudo iniciar sesión: {auth.error.message}
          </p>
        )}

        {!auth.isLoading && (
          <button
            onClick={() => auth.signinRedirect()}
            style={{
              width: '100%',
              padding: '12px 16px',
              borderRadius: 10,
              border: 'none',
              background: 'var(--accent)',
              color: 'var(--accent-contrast)',
              fontWeight: 600,
              fontSize: 15,
            }}
          >
            Iniciar sesión
          </button>
        )}
      </div>
    </div>
  );
}

function AuthGate() {
  const auth = useAuth();

  useEffect(() => {
    setAuthToken(auth.user?.access_token ?? null);
    setOwnerId(auth.user?.profile.sub ?? null);
  }, [auth.user]);

  useEffect(() => {
    return auth.events.addAccessTokenExpired(() => {
      void auth.signinRedirect();
    });
  }, [auth.events, auth.signinRedirect]);

  async function handleLogout() {
    // Cognito Hosted UI no implementa el end_session_endpoint estándar de OIDC,
    // así que hay que pegarle a su /logout a mano — pero eso no limpia el usuario
    // que oidc-client-ts guarda en sessionStorage. Sin este removeUser(), al volver
    // de Cognito la SPA puede seguir viéndote "logueado" con el token viejo.
    await auth.removeUser();
    window.location.href = cognitoLogoutUrl();
  }

  if (!auth.isAuthenticated) {
    return <LoginScreen />;
  }

  return <AppShell userEmail={auth.user?.profile.email} onLogout={() => void handleLogout()} />;
}

export default function App() {
  return authEnabled ? <AuthGate /> : <AppShell />;
}
