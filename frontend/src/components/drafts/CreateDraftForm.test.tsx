import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import CreateDraftForm from './CreateDraftForm'
import { MAX_GOAL_LENGTH, MAX_TITLE_LENGTH } from './DraftTypes'

afterEach(() => {
  cleanup()
})

const SUBMIT_LABEL = 'Kreiraj nacrt'

function titleInput() {
  return screen.getByLabelText('Naziv') as HTMLInputElement
}

function goalInput() {
  return screen.getByLabelText('Cilj zadatka') as HTMLTextAreaElement
}

function typeSelect() {
  return screen.getByLabelText('Tip laboratorije') as HTMLSelectElement
}

function submit() {
  // Klik na dugme (a ne fireEvent.submit) prolazi kroz browserovu validaciju,
  // pa hvata slučaj kada native poruka guši našu.
  fireEvent.click(screen.getByRole('button', { name: SUBMIT_LABEL }))
}

describe('AUT-01 CreateDraftForm', () => {
  it('prikazuje sva polja forme', () => {
    render(<CreateDraftForm onCreate={vi.fn()} />)

    expect(titleInput().tagName).toBe('INPUT')
    expect(goalInput().tagName).toBe('TEXTAREA')
    expect(typeSelect().tagName).toBe('SELECT')
  })

  it('prikazuje sve tipove laboratorija', () => {
    render(<CreateDraftForm onCreate={vi.fn()} />)

    expect(typeSelect().options).toHaveLength(8)
  })

  it('prazan naziv ne dozvoljava kreiranje i prikazuje našu poruku', () => {
    const onCreate = vi.fn()
    render(<CreateDraftForm onCreate={onCreate} />)

    submit()

    expect(onCreate).not.toHaveBeenCalled()
    expect(screen.getByRole('alert').textContent).toContain('Unesite naziv')
    expect(titleInput().getAttribute('aria-invalid')).toBe('true')
  })

  it('naziv koji sadrži samo razmake nije validan', () => {
    const onCreate = vi.fn()
    render(<CreateDraftForm onCreate={onCreate} />)

    fireEvent.change(titleInput(), { target: { value: '   ' } })
    submit()

    expect(onCreate).not.toHaveBeenCalled()
    expect(screen.getByRole('alert')).not.toBeNull()
  })

  it('greška nestaje nakon ispravnog unosa', () => {
    render(<CreateDraftForm onCreate={vi.fn()} />)

    submit()
    expect(screen.getByRole('alert')).not.toBeNull()

    fireEvent.change(titleInput(), { target: { value: 'Moj zadatak' } })
    submit()

    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('naziv i cilj imaju ograničenje dužine', () => {
    render(<CreateDraftForm onCreate={vi.fn()} />)

    expect(titleInput().getAttribute('maxlength')).toBe(String(MAX_TITLE_LENGTH))
    expect(goalInput().getAttribute('maxlength')).toBe(String(MAX_GOAL_LENGTH))
  })

  it('prosleđuje ispravne podatke kroz onCreate', () => {
    const onCreate = vi.fn()
    render(<CreateDraftForm onCreate={onCreate} />)

    fireEvent.change(titleInput(), { target: { value: '  Moj zadatak  ' } })
    fireEvent.change(goalInput(), {
      target: { value: '  Testiranje kontrole pristupa  ' },
    })
    fireEvent.change(typeSelect(), { target: { value: 'LogAnalysis' } })

    submit()

    expect(onCreate).toHaveBeenCalledTimes(1)
    expect(onCreate).toHaveBeenCalledWith({
      title: 'Moj zadatak',
      goal: 'Testiranje kontrole pristupa',
      type: 'LogAnalysis',
    })
  })

  it('cilj koji sadrži samo razmake se šalje kao prazan', () => {
    const onCreate = vi.fn()
    render(<CreateDraftForm onCreate={onCreate} />)

    fireEvent.change(titleInput(), { target: { value: 'Moj zadatak' } })
    fireEvent.change(goalInput(), { target: { value: '   ' } })

    submit()

    expect(onCreate).toHaveBeenCalledWith(
      expect.objectContaining({ goal: '' }),
    )
  })

  it('nakon kreiranja resetuje sva polja forme', () => {
    render(<CreateDraftForm onCreate={vi.fn()} />)

    fireEvent.change(titleInput(), { target: { value: 'Moj zadatak' } })
    fireEvent.change(goalInput(), { target: { value: 'Neki cilj' } })
    fireEvent.change(typeSelect(), { target: { value: 'ThreatModel' } })

    submit()

    expect(titleInput().value).toBe('')
    expect(goalInput().value).toBe('')
    expect(typeSelect().value).toBe('AccessControl')
  })
})
