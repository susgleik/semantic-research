import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import { AuthProvider } from 'react-oidc-context'
import './index.css'
import App from './App.tsx'
import { authEnabled, cognitoAuthConfig } from './auth/config'

const root = (
  <StrictMode>
    <BrowserRouter>
      <App />
    </BrowserRouter>
  </StrictMode>
)

createRoot(document.getElementById('root')!).render(
  authEnabled ? <AuthProvider {...cognitoAuthConfig}>{root}</AuthProvider> : root,
)
