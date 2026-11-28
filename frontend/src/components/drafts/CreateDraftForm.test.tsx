import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import type { ComponentProps } from 'react'
import CreateDraftForm from './CreateDraftForm'
import { MAX_GOAL_LENGTH, MAX_TITLE_LENGTH } from './DraftTypes'

afterEach(() => {
  cleanup()
})

type FormProps = ComponentProps<typeof CreateDraftForm>

function renderForm(overrides: Partial<FormProps> = {}) {
  const onCreate = overrides.onCreate ?? vi.fn().mockResolvedValue(true)
  render(<CreateDraftForm {...overrides} onCreate={onCreate} />)
  return onCreate
}

function titleInput() {
  return screen.getByLabelText('Naziv') as HTMLInputElement
}

function goalInput() {
  return screen.getByLabelText('Cilj zadatka') as HTMLTextAreaElement
}

function typeSelect() {
  return screen.getByLabelText('Tip laboratorije') as HTMLSelectElement
}

// Klik na dugme (a ne fireEvent.submit) prolazi kroz browserovu validaciju,
// pa hvata slučaj kada native poruka guši našu.
async function submit() {
  await act(async () => {
    fireEvent.click(screen.getByRole('button', { name: 'Kreiraj nacrt' }))
  })
}

describe('AUT-01 CreateDraftForm', () => {
  it('prikazuje sva polja forme', () => {
    renderForm()

    expect(titleInput().tagName).toBe('INPUT')
    expect(goalInput().tagName).toBe('TEXTAREA')
    expect(typeSelect().tagName).toBe('SELECT')
  })

  it('prikazuje sve tipove laboratorija', () => {
    renderForm()

    expect(typeSelect().options).toHaveLength(8)
  })

  it('prazan naziv ne dozvoljava kreiranje i prikazuje našu poruku', async () => {
    const onCreate = renderForm()

    await submit()

    expect(onCreate).not.toHaveBeenCalled()
    expect(screen.getByRole('alert').textContent).toContain('Unesite naziv')
    expect(titleInput().getAttribute('aria-invalid')).toBe('true')
  })

  it('naziv koji sadrži samo razmake nije validan', async () => {
    const onCreate = renderForm()

    fireEvent.change(titleInput(), { target: { value: '   ' } })
    await submit()

    expect(onCreate).not.toHaveBeenCalled()
    expect(screen.getByRole('alert')).not.toBeNull()
  })

  it('greška naziva nestaje nakon ispravnog unosa', async () => {
    renderForm()

    await submit()
    expect(screen.getByRole('alert')).not.toBeNull()

    fireEvent.change(titleInput(), { target: { value: 'Moj zadatak' } })
    await submit()

    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('naziv i cilj imaju ograničenje dužine', () => {
    renderForm()

    expect(titleInput().getAttribute('maxlength')).toBe(String(MAX_TITLE_LENGTH))
    expect(goalInput().getAttribute('maxlength')).toBe(String(MAX_GOAL_LENGTH))
  })

  it('prosleđuje ispravne podatke kroz onCreate', async () => {
    const onCreate = renderForm()

    fireEvent.change(titleInput(), { target: { value: '  Moj zadatak  ' } })
    fireEvent.change(goalInput(), { target: { value: '  Testiranje kontrole pristupa  ' } })
    fireEvent.change(typeSelect(), { target: { value: 'LogAnalysis' } })

    await submit()

    expect(onCreate).toHaveBeenCalledTimes(1)
    expect(onCreate).toHaveBeenCalledWith({
      title: 'Moj zadatak',
      goal: 'Testiranje kontrole pristupa',
      type: 'LogAnalysis',
    })
  })

  it('cilj koji sadrži samo razmake se šalje kao prazan', async () => {
    const onCreate = renderForm()

    fireEvent.change(titleInput(), { target: { value: 'Moj zadatak' } })
    fireEvent.change(goalInput(), { target: { value: '   ' } })
    await submit()

    expect(onCreate).toHaveBeenCalledWith(expect.objectContaining({ goal: '' }))
  })

  it('nakon uspešnog kreiranja resetuje sva polja forme', async () => {
    renderForm({ onCreate: vi.fn().mockResolvedValue(true) })

    fireEvent.change(titleInput(), { target: { value: 'Moj zadatak' } })
    fireEvent.change(goalInput(), { target: { value: 'Neki cilj' } })
    fireEvent.change(typeSelect(), { target: { value: 'ThreatModel' } })
    await submit()

    expect(titleInput().value).toBe('')
    expect(goalInput().value).toBe('')
    expect(typeSelect().value).toBe('AccessControl')
  })

  it('kada kreiranje ne uspe, uneti podaci se ne brišu', async () => {
    renderForm({ onCreate: vi.fn().mockResolvedValue(false) })

    fireEvent.change(titleInput(), { target: { value: 'Moj zadatak' } })
    fireEvent.change(goalInput(), { target: { value: 'Neki cilj' } })
    fireEvent.change(typeSelect(), { target: { value: 'ThreatModel' } })
    await submit()

    expect(titleInput().value).toBe('Moj zadatak')
    expect(goalInput().value).toBe('Neki cilj')
    expect(typeSelect().value).toBe('ThreatModel')
  })

  it('tokom slanja dugme je onemogućeno i ne dozvoljava novo slanje', () => {
    const onCreate = renderForm({ submitting: true })

    const button = screen.getByRole('button', { name: 'Kreiranje…' }) as HTMLButtonElement
    fireEvent.change(titleInput(), { target: { value: 'Moj zadatak' } })
    fireEvent.click(button)

    expect(button.disabled).toBe(true)
    expect(onCreate).not.toHaveBeenCalled()
  })

  it('prikazuje grešku servera koju prosledi stranica', () => {
    renderForm({ error: 'Nemate pravo da radite sa nacrtima zadataka.' })

    expect(screen.getByRole('alert').textContent).toContain('Nemate pravo')
  })
})
