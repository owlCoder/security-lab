import LoginForm from '../components/LoginForm'
import { OidcFailureNotice, OidcLoginButton } from '../components/OidcLogin'

type Props = { onSuccess: () => boolean }

export default function LoginPage({ onSuccess }: Props) {
  return <section className="login-page">
    <h2>Log in</h2>
    <OidcFailureNotice />
    <LoginForm onSuccess={onSuccess} />
    <OidcLoginButton />
  </section>
}