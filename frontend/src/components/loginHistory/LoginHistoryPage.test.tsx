import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import App from '../../App'
// IDN-22 TIM 09

type Item = { occurredAt: string; succeeded: boolean; maskedSourceAddress: string }

const SUCCEEDED: Item = { occurredAt: '2026-11-10T09:00:00.000Z', succeeded: true, maskedSourceAddress: '203.0.113.*' }
const FAILED: Item = { occurredAt: '2026-11-09T08:30:00.000Z', succeeded: false, maskedSourceAddress: '2001:db8:85a3:8d3::*' }
const SIGNED_IN_USER = { userId: '10000000-0000-0000-0000-000000000001', level: 'Active' }

function json(status: number, body: unknown, headers: Record<string, string> = {}) {
  return new Response(JSON.stringify(body), { status, headers })
}

function page(items: Item[], pageNumber = 1, pageSize = 20) {
  return json(200, { items, page: pageNumber, pageSize })
}

// Returns a mock that records only login history calls; the session check (/api/auth/me) answers with a signed-in user.
function serverResponds(...responses: Response[]) {
  const queue = [...responses]
  // The last response repeats; clone() because a Response body can be read only once.
  const fetchMock = vi.fn(async (_url: string, _init?: RequestInit) => (queue.length > 1 ? queue.shift() ?? queue[0] : queue[0]).clone())
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    if (url === '/api/auth/me') return json(200, SIGNED_IN_USER)
    if (url.startsWith('/api/auth/login-history')) return fetchMock(url, init)
    return json(404, {})
  }))
  return fetchMock
}

// The page runs inside the real AuthProvider and ProtectedRoute, as in the app.
async function renderPage() {
  render(<MemoryRouter initialEntries={['/login-history']}><App /></MemoryRouter>)
  await screen.findByRole('heading', { level: 1, name: 'Istorija prijava' })
}

function query(fetchMock: ReturnType<typeof serverResponds>, call: number): URLSearchParams {
  return new URL(String(fetchMock.mock.calls[call][0]), 'http://localhost').searchParams
}

async function renderLoaded() {
  await renderPage()
  await waitFor(() => expect(screen.getByRole('status').textContent).not.toContain('Učitavanje'))
}

function setDates(from: string, to: string) {
  fireEvent.change(screen.getByLabelText('Od'), { target: { value: from } })
  fireEvent.change(screen.getByLabelText('Do'), { target: { value: to } })
}

function submit() {
  fireEvent.submit(screen.getByRole('search', { name: 'Filter istorije prijava' }))
}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('IDN-22 login history page', () => {
  it('render_WhenServerReturnsItems_ShowsTimeOutcomeAndMaskedAddress', async () => {
    serverResponds(page([SUCCEEDED, FAILED]))

    await renderLoaded()

    expect(screen.getByRole('table', { name: 'Vaše prijave' })).toBeTruthy()
    expect(screen.getByText(new Date(SUCCEEDED.occurredAt).toLocaleString('sr-Latn-RS'))).toBeTruthy()
    expect(screen.getByText('Uspešna')).toBeTruthy()
    expect(screen.getByText('Neuspešna')).toBeTruthy()
    expect(screen.getByText('203.0.113.*')).toBeTruthy()
    expect(screen.getByText('2001:db8:85a3:8d3::*')).toBeTruthy()
    expect(screen.getByRole('status').textContent).toContain('Pronađeno: 2')
  })

  it('render_OnOpen_LoadsFirstPageWithoutDates', async () => {
    const fetchMock = serverResponds(page([]))

    await renderLoaded()

    const params = query(fetchMock, 0)
    expect(Object.fromEntries(params)).toEqual({ page: '1', pageSize: '20' })
  })

  it('render_WhenResultEmpty_ShowsEmptyMessageNotError', async () => {
    serverResponds(page([]))

    await renderLoaded()

    expect(screen.getByText('Nema prijava za izabrani period.')).toBeTruthy()
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('submit_WhenFailedOutcomeAndDatesChosen_SendsOutcomeAndIsoBounds', async () => {
    const fetchMock = serverResponds(page([]))
    await renderLoaded()

    setDates('2026-11-01', '2026-11-10')
    fireEvent.click(screen.getByLabelText('Neuspešne'))
    submit()

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2))
    const params = query(fetchMock, 1)
    expect(params.get('outcome')).toBe('failed')
    expect(params.get('from')).toBe(new Date(2026, 10, 1, 0, 0, 0, 0).toISOString())
    expect(params.get('to')).toBe(new Date(2026, 10, 10, 23, 59, 59, 999).toISOString())
    expect(params.get('page')).toBe('1')
  })

  it('submit_WhenFromAfterTo_ShowsMessageAndDoesNotCallServer', async () => {
    const fetchMock = serverResponds(page([]))
    await renderLoaded()

    setDates('2026-11-10', '2026-11-01')
    submit()

    expect((await screen.findByRole('alert')).textContent).toBe('Početni datum mora biti pre krajnjeg.')
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('submit_WhenRangeLongerThan90Days_ShowsMessageAndDoesNotCallServer', async () => {
    const fetchMock = serverResponds(page([]))
    await renderLoaded()

    setDates('2026-01-01', '2026-11-01')
    submit()

    expect((await screen.findByRole('alert')).textContent).toBe('Period može biti najviše 90 dana.')
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('submit_WhenDateOnlyPartiallyTyped_ShowsMessageAndDoesNotCallServer', async () => {
    const fetchMock = serverResponds(page([]))
    await renderLoaded()
    // A partially typed date reports value "" and validity.badInput in the browser; jsdom cannot type segments.
    Object.defineProperty(screen.getByLabelText('Od'), 'validity', { value: { badInput: true }, configurable: true })

    submit()

    expect((await screen.findByRole('alert')).textContent).toBe('Filter nije ispravan. Proverite unete vrednosti.')
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('load_WhenServerReturnsRangeTooLarge_ShowsOwnMessageNotServerText', async () => {
    serverResponds(json(400, { code: 'range_too_large', message: 'SECRET stack trace at Npgsql' }))

    await renderPage()

    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toBe('Period može biti najviše 90 dana.')
    expect(document.body.textContent).not.toContain('SECRET')
    expect(document.body.textContent).not.toContain('range_too_large')
  })

  it('load_WhenServerReturns401SessionInvalid_SignsOutAndRedirectsToLogin', async () => {
    serverResponds(json(401, { code: 'session_invalid', message: 'token abc revoked' }))

    render(<MemoryRouter initialEntries={['/login-history']}><App /></MemoryRouter>)

    await screen.findByRole('heading', { name: 'Log in' })
    expect(screen.queryByRole('heading', { name: 'Istorija prijava' })).toBeNull()
    expect(screen.queryByRole('link', { name: 'Istorija prijava' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Log out' })).toBeNull()
    expect(document.body.textContent).not.toContain('abc')
  })

  it('load_WhenServerReturns429_ShowsRetryAfterSeconds', async () => {
    serverResponds(json(429, { code: 'too_many_requests' }, { 'Retry-After': '30' }))

    await renderPage()

    expect((await screen.findByRole('alert')).textContent).toBe('Previše zahteva. Pokušajte ponovo za 30 s.')
  })

  it('load_WhenServerFailsUnexpectedly_ShowsGenericMessage', async () => {
    serverResponds(json(500, { code: 'db_down', message: 'Connection string Host=db;Password=x' }))

    await renderPage()

    expect((await screen.findByRole('alert')).textContent).toBe('Istorija prijava trenutno nije dostupna. Pokušajte kasnije.')
    expect(document.body.textContent).not.toContain('Password')
  })

  it('paging_WhenFewerItemsThanPageSize_DisablesNextAndPreviousOnFirstPage', async () => {
    serverResponds(page([SUCCEEDED]))

    await renderLoaded()

    expect((screen.getByRole('button', { name: 'Sledeća' }) as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByRole('button', { name: 'Prethodna' }) as HTMLButtonElement).disabled).toBe(true)
    expect(screen.getByText('Strana 1')).toBeTruthy()
  })

  it('paging_WhenNextClicked_RequestsPage2WithAppliedFilter', async () => {
    const full = Array.from({ length: 10 }, () => SUCCEEDED)
    const fetchMock = serverResponds(page(full, 1, 10), page(full, 1, 10), page([FAILED], 2, 10))
    await renderLoaded()
    fireEvent.change(screen.getByLabelText('Broj prijava po strani'), { target: { value: '10' } })
    fireEvent.click(screen.getByLabelText('Neuspešne'))
    submit()
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2))
    await waitFor(() => expect((screen.getByRole('button', { name: 'Sledeća' }) as HTMLButtonElement).disabled).toBe(false))

    fireEvent.click(screen.getByLabelText('Sve'))
    fireEvent.click(screen.getByRole('button', { name: 'Sledeća' }))

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(3))
    const params = query(fetchMock, 2)
    expect(params.get('page')).toBe('2')
    expect(params.get('pageSize')).toBe('10')
    expect(params.get('outcome')).toBe('failed')
    expect(await screen.findByText('Strana 2')).toBeTruthy()
  })

  it('keyboard_WhenEnterInDateField_SubmitsFormThroughSubmitButton', async () => {
    const fetchMock = serverResponds(page([]))
    await renderLoaded()
    const from = screen.getByLabelText('Od') as HTMLInputElement

    fireEvent.change(from, { target: { value: '2026-11-01' } })
    // jsdom has no implicit submission on Enter; the browser submits the input's form through its submit button.
    const form = from.form as HTMLFormElement
    expect(form.querySelector('button[type="submit"]')?.textContent).toBe('Prikaži')
    fireEvent.submit(form)

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2))
    expect(query(fetchMock, 1).get('from')).toBe(new Date(2026, 10, 1).toISOString())
  })

  it('accessibility_AllControlsAreButtonsAndInputsHaveNames', async () => {
    serverResponds(page([SUCCEEDED]))

    await renderLoaded()

    for (const button of screen.getAllByRole('button')) {
      expect(button.tagName).toBe('BUTTON')
      expect(button.getAttribute('type')).toMatch(/^(button|submit)$/)
    }
    for (const label of ['Od', 'Do', 'Sve', 'Uspešne', 'Neuspešne', 'Broj prijava po strani']) {
      expect(screen.getByLabelText(label)).toBeTruthy()
    }
    expect(screen.getByRole('group', { name: 'Ishod' })).toBeTruthy()
    expect(screen.getAllByRole('columnheader').map(th => th.textContent)).toEqual(['Vreme', 'Ishod', 'Adresa'])
  })

  it('search_AfterResults_MovesFocusToResultsCaption', async () => {
    serverResponds(page([SUCCEEDED]))
    await renderLoaded()

    submit()

    await waitFor(() => expect(document.activeElement?.textContent).toBe('Vaše prijave'))
  })

  it('reset_WhenClicked_ClearsFormAndReloadsFirstPage', async () => {
    const fetchMock = serverResponds(page([]))
    await renderLoaded()
    setDates('2026-11-01', '2026-11-10')
    fireEvent.click(screen.getByLabelText('Uspešne'))

    fireEvent.click(screen.getByRole('button', { name: 'Poništi' }))

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2))
    expect(Object.fromEntries(query(fetchMock, 1))).toEqual({ page: '1', pageSize: '20' })
    expect((screen.getByLabelText('Od') as HTMLInputElement).value).toBe('')
    expect((screen.getByLabelText('Sve') as HTMLInputElement).checked).toBe(true)
  })
})
