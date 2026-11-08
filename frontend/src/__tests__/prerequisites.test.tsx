
/**
 * LAB-06 - testovi nastavnickog ekrana za preduslove.
 * API pozivi su simulirani, bez potrebe za backendom.
 */

// @vitest-environment jsdom

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'

import PrerequisitesEditor from '../components/prerequisites/PrerequisitesEditor'
import PrerequisitesList from '../components/prerequisites/PrerequisitesList'
import {
  getPrerequisites,
  addPrerequisite,
  removePrerequisite,
} from '../api/prerequisites'

// Simuliramo API kako testovi ne bi zavisili od servera.
vi.mock('../api/prerequisites', () => ({
  getPrerequisites: vi.fn(),
  addPrerequisite: vi.fn(),
  removePrerequisite: vi.fn(),
}))

const mockGet = vi.mocked(getPrerequisites)
const mockAdd = vi.mocked(addPrerequisite)
const mockRemove = vi.mocked(removePrerequisite)

const laboratoryId = 'lab-1'
const requiredId = 'lab-2'

describe('LAB-06 - nastavnicki ekran za preduslove', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  afterEach(() => {
    cleanup()
  })

  // TEST 1: Lista prikazuje preduslove.
  it('prikazuje postojece preduslove', async () => {
    mockGet.mockResolvedValue({
      ok: true,
      data: [requiredId],
    })

    render(<PrerequisitesEditor laboratoryId={laboratoryId} />)

    expect(await screen.findByText(requiredId)).toBeTruthy()

    expect(mockGet).toHaveBeenCalledWith(laboratoryId)
  })

  // TEST 2: Dodavanje osvezava listu.
  it('osvezava listu nakon dodavanja preduslova', async () => {
    mockGet
      .mockResolvedValueOnce({
        ok: true,
        data: [],
      })
      .mockResolvedValueOnce({
        ok: true,
        data: [requiredId],
      })

    mockAdd.mockResolvedValue({
      ok: true,
      data: {
        laboratoryId,
        requiredLaboratoryId: requiredId,
      },
    })

    render(<PrerequisitesEditor laboratoryId={laboratoryId} />)

    await screen.findByText(
      'Laboratorija nema definisane preduslove.'
    )

    const input = screen.getByLabelText(
      'ID laboratorije koja je preduslov'
    )

    fireEvent.change(input, {
      target: { value: requiredId },
    })

    fireEvent.click(
      screen.getByRole('button', { name: 'Dodaj preduslov' })
    )

    await waitFor(() => {
      expect(mockAdd).toHaveBeenCalledWith(
        laboratoryId,
        requiredId
      )
    })

    expect(await screen.findByText(requiredId)).toBeTruthy()
    expect(mockGet).toHaveBeenCalledTimes(2)
  })

  // TEST 3: Odbijena izmena prikazuje gresku.
  it('prikazuje gresku kada server odbije dodavanje', async () => {
    mockGet.mockResolvedValue({
      ok: true,
      data: [],
    })

    mockAdd.mockResolvedValue({
      ok: false,
      error: {
        status: 403,
        code: 'forbidden',
        message: 'Nemate dozvolu za ovu radnju.',
      },
    })

    render(<PrerequisitesEditor laboratoryId={laboratoryId} />)

    await screen.findByText(
      'Laboratorija nema definisane preduslove.'
    )

    fireEvent.change(
      screen.getByLabelText('ID laboratorije koja je preduslov'),
      { target: { value: requiredId } }
    )

    fireEvent.click(
      screen.getByRole('button', { name: 'Dodaj preduslov' })
    )

    const alert = await screen.findByRole('alert')

    expect(alert.textContent).toContain(
      'Nemate dozvolu za ovu radnju.'
    )

    expect(mockGet).toHaveBeenCalledTimes(1)
    expect(mockRemove).not.toHaveBeenCalled()
  })
})

describe('LAB-06 - prikaz preduslova na detaljima laboratorije', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  afterEach(() => {
    cleanup()
  })

  it('prikazuje preduslove laboratorije', async () => {
    mockGet.mockResolvedValue({
      ok: true,
      data: ['lab-2'],
    })

    render(<PrerequisitesList laboratoryId="lab-1" />)

    expect(
      await screen.findByText('Laboratorija: lab-2')
    ).toBeTruthy()

    expect(mockGet).toHaveBeenCalledWith('lab-1')
  })

  it('prikazuje nazive laboratorija kada su dostupni', async () => {
    mockGet.mockResolvedValue({
      ok: true,
      data: ['lab-2'],
    })

    render(
      <PrerequisitesList
        laboratoryId="lab-1"
        laboratoryNames={{
          'lab-2': 'Osnove informacione bezbednosti',
        }}
      />
    )

    expect(
      await screen.findByText('Osnove informacione bezbednosti')
    ).toBeTruthy()
  })

  it('prikazuje gresku kada server odbije citanje', async () => {
    mockGet.mockResolvedValue({
      ok: false,
      error: {
        status: 403,
        code: 'forbidden',
        message: 'Nemate dozvolu za ovu radnju.',
      },
    })

    render(<PrerequisitesList laboratoryId="lab-1" />)

    const alert = await screen.findByRole('alert')

    expect(alert.textContent).toContain(
      'Nemate dozvolu za ovu radnju.'
    )
  })
})

