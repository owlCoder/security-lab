import LoginForm from './LoginForm'

type Props = { onSuccess: () => boolean }

export default function LoginPage({ onSuccess }: Props) {
  return <section className="login-page">
    <h2>Log in</h2>
    <LoginForm onSuccess={onSuccess} />
  </section>
}