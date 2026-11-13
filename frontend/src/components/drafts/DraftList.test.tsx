import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import DraftList from './DraftList'
import type { Draft } from './DraftTypes'

afterEach(() => {
  cleanup()
})

// Naslovi namerno nisu isti kao nazivi tipova, da getByText ne nađe dva elementa.
const drafts: Draft[] = [
  {
    id: 'draft-1',
    title: 'Pristup nastavnika',
    goal: 'Testiranje prava pristupa',
    type: 'AccessControl',
    status: 'Draft',
  },
  {
    id: 'draft-2',
    title: 'Pregled bezbednosnih događaja',
    goal: 'Analiza bezbednosnih događaja',
    type: 'LogAnalysis',
    status: 'Published',
  },
]

describe('AUT-01 DraftList', () => {
  it('prikazuje poruku kada nema nacrta', () => {
    render(<DraftList drafts={[]} onOpen={vi.fn()} />)

    expect(screen.getByText(/nemate nijedan nacrt/i)).not.toBeNull()
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('prikazuje sve prosleđene nacrte', () => {
    render(<DraftList drafts={drafts} onOpen={vi.fn()} />)

    expect(screen.getByText('Pristup nastavnika')).not.toBeNull()
    expect(screen.getByText('Pregled bezbednosnih događaja')).not.toBeNull()
    expect(screen.getAllByRole('button', { name: /^Otvori nacrt/ })).toHaveLength(2)
  })

  it('prikazuje čitljive nazive tipa i statusa, ne interne vrednosti', () => {
    render(<DraftList drafts={drafts} onOpen={vi.fn()} />)

    expect(screen.getByText('Kontrola pristupa')).not.toBeNull()
    expect(screen.getByText('Analiza logova')).not.toBeNull()
    expect(screen.getByText('Nacrt')).not.toBeNull()
    expect(screen.getByText('Objavljena')).not.toBeNull()
    expect(screen.queryByText('AccessControl')).toBeNull()
  })

  it('dugme za otvaranje ima jedinstven pristupačan naziv', () => {
    render(<DraftList drafts={drafts} onOpen={vi.fn()} />)

    expect(
      screen.getByRole('button', { name: 'Otvori nacrt Pristup nastavnika' }),
    ).not.toBeNull()
  })

  it('poziva onOpen sa ID-jem izabranog nacrta', () => {
    const onOpen = vi.fn()
    render(<DraftList drafts={drafts} onOpen={onOpen} />)

    fireEvent.click(
      screen.getByRole('button', { name: 'Otvori nacrt Pregled bezbednosnih događaja' }),
    )

    expect(onOpen).toHaveBeenCalledTimes(1)
    expect(onOpen).toHaveBeenCalledWith('draft-2')
  })
})
