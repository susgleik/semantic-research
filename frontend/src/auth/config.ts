import type { AuthProviderProps } from 'react-oidc-context';

const userPoolId = import.meta.env.VITE_COGNITO_USER_POOL_ID as string | undefined;
const clientId = import.meta.env.VITE_COGNITO_CLIENT_ID as string | undefined;
const domain = import.meta.env.VITE_COGNITO_DOMAIN as string | undefined;
const region = userPoolId?.split('_')[0];

export const authEnabled = Boolean(userPoolId && clientId && domain);

// Cognito expone el discovery document OIDC en la URL del User Pool (el `authority`),
// pero el authorization_endpoint que ese documento anuncia vive en el dominio del
// Hosted UI (`cognito_domain`, Terraform Fase 10) — react-oidc-context resuelve ambos
// automáticamente a partir del `authority`.
export const cognitoAuthConfig: AuthProviderProps = {
  authority: `https://cognito-idp.${region}.amazonaws.com/${userPoolId}`,
  client_id: clientId ?? '',
  redirect_uri: window.location.origin,
  post_logout_redirect_uri: window.location.origin,
  response_type: 'code',
  scope: 'openid email profile',
  // Cognito Hosted UI no soporta prompt=none, así que el silent renew por iframe
  // que react-oidc-context intenta por default siempre falla y termina limpiando
  // la sesión antes de que el access token realmente expire. Se maneja la
  // expiración con un re-login explícito en vez de renovación silenciosa.
  automaticSilentRenew: false,
};

export function cognitoLogoutUrl(): string {
  const params = new URLSearchParams({
    client_id: clientId ?? '',
    logout_uri: window.location.origin,
  });
  return `https://${domain}/logout?${params.toString()}`;
}
