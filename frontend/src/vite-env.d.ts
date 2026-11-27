/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** IDN-08: "true" shows the OIDC login button. */
  readonly VITE_OIDC_ENABLED?: string
}