import CreateAccountForm from './CreateAccountForm'
import SessionNotificationSettings from './SessionNotificationSettings'

export default function ProtectedApp() {
  return <>
    <h1>Security Lab</h1>
    <p>An interactive laboratory platform for the Fundamentals of Information Security course. Develop features through the assigned project components.</p>
    <SessionNotificationSettings />
    <section><h2>Kreiranje naloga (administrator)</h2><CreateAccountForm /></section>
  </>
}
