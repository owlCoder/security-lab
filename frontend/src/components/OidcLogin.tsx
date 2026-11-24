import { useSearchParams } from 'react-router-dom'

/** IDN-08: show the button only when the API runs with Oidc:Enabled = true (VITE_OIDC_ENABLED=true locally). */
export const OIDC_LOGIN_ENABLED = import.meta.env.VITE_OIDC_ENABLED === 'true'

/** Backend endpoint that starts the OIDC login. A full-page navigation, never fetch(): it redirects to the provider. */
export const OIDC_START_PATH = '/api/auth/oidc/start'

/** One generic text for every refusal; the real reason is only in the server audit log. */
export const OIDC_FAILED_MESSAGE =
  'Sign-in with the external account failed. Try again or use your email and password.'

type ButtonProps = { enabled?: boolean }

export function OidcLoginButton({ enabled = OIDC_LOGIN_ENABLED }: ButtonProps) {
  if (!enabled) return null
  return <a className="oidc-login" href={OIDC_START_PATH}>Sign in with external account (OIDC)</a>
}

/** The backend redirects every refused OIDC login to /login?oidc=failed. Nothing from the URL is ever rendered. */
export function OidcFailureNotice() {
  const [params] = useSearchParams()
  if (params.get('oidc') !== 'failed') return null
  return <p className="login-error" role="alert">{OIDC_FAILED_MESSAGE}</p>
}