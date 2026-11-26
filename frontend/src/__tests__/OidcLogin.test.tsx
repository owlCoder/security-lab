import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import {
  OIDC_FAILED_MESSAGE,
  OIDC_START_PATH,
  OidcFailureNotice,
  OidcLoginButton,
} from '../components/OidcLogin'

function renderNotice(url: string) {
  render(<MemoryRouter initialEntries={[url]}><OidcFailureNotice /></MemoryRouter>)
}

describe('IDN-08 OIDC login UI', () => {
  afterEach(cleanup)

  it('hides the button when OIDC is disabled', () => {
    render(<OidcLoginButton enabled={false} />)
    expect(screen.queryByRole('link')).toBeNull()
  })

  it('the button is a plain link to the backend start endpoint', () => {
    render(<OidcLoginButton enabled />)
    expect(screen.getByRole('link').getAttribute('href')).toBe(OIDC_START_PATH)
  })

  it('shows one generic message after a refused login', () => {
    renderNotice('/login?oidc=failed')
    expect(screen.getByRole('alert').textContent).toBe(OIDC_FAILED_MESSAGE)
  })

  it.each([
    '/login',
    '/login?oidc=ok',
    '/login?oidc=%3Cscript%3Ealert(1)%3C%2Fscript%3E',
  ])('shows nothing and echoes nothing from the URL for %s', url => {
    renderNotice(url)
    expect(screen.queryByRole('alert')).toBeNull()
    expect(document.body.textContent).not.toContain('script')
  })
})