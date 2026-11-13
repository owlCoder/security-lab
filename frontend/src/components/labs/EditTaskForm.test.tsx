import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EditTaskForm } from './EditTaskForm'

const task = {
  id: 'task-42',
  title: 'Početni naslov',
  description: 'Početni opis',
  goal: 'Početni opis',
  type: 'AccessControl',
}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

function prepareWrite(status = 204) {
  const fetchMock = vi.fn()
    .mockResolvedValueOnce(new Response(JSON.stringify(task), { status: 200 }))
    .mockResolvedValueOnce(new Response(null, { status }))
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

describe('AUT-01 EditTaskForm', () => {
  it('prikazuje neuspeh API čuvanja umesto lažnog uspeha', async () => {
    prepareWrite(403)
    render(<EditTaskForm taskId={task.id} />)
    await screen.findByDisplayValue(task.title)
    fireEvent.click(screen.getByRole('button', { name: 'Sačuvaj izmene' }))
    expect((await screen.findByRole('alert')).textContent).toContain('HTTP 403')
    expect(screen.queryByText(/uspešno sačuvane/i)).toBeNull()
  })

  it('mrežna greška nikada nije prikazana kao uspešno čuvanje', async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify(task), { status: 200 }))
      .mockRejectedValueOnce(new Error('Mreža nije dostupna'))
    vi.stubGlobal('fetch', fetchMock)
    render(<EditTaskForm taskId={task.id} />)
    await screen.findByDisplayValue(task.title)
    fireEvent.click(screen.getByRole('button', { name: 'Sačuvaj izmene' }))
    expect((await screen.findByRole('alert')).textContent).toContain('Mreža nije dostupna')
    expect(screen.queryByText(/uspešno sačuvane/i)).toBeNull()
  })

  it('potvrđuje čuvanje tek kada ga server potvrdi i ID ostaje neizmenjen', async () => {
    const fetchMock = prepareWrite()
    render(<EditTaskForm taskId={task.id} />)
    await screen.findByDisplayValue(task.title)
    fireEvent.change(screen.getByLabelText('Naslov zadatka'), { target: { value: 'Novi naslov' } })
    fireEvent.change(screen.getByLabelText('Opis zadatka'), { target: { value: 'Novi opis' } })
    fireEvent.change(screen.getByLabelText('Tip zadatka'), { target: { value: 'LogAnalysis' } })
    fireEvent.click(screen.getByRole('button', { name: 'Sačuvaj izmene' }))
    expect((await screen.findByText('Izmene su uspešno sačuvane.')).textContent).toContain('uspešno sačuvane')
    const [, options] = fetchMock.mock.calls[1]
    expect(options.method).toBe('PUT')
    expect(JSON.parse(options.body)).toEqual({
      title: 'Novi naslov',
      description: 'Novi opis',
      goal: 'Novi opis',
      type: 'LogAnalysis',
    })
    expect(fetchMock.mock.calls[1][0]).toBe('/api/authoring/tasks/task-42')
  })

  it('prikazuje formu i grešku kada zadatak ne može da se učita', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 404 }))
    vi.stubGlobal('fetch', fetchMock)
    render(<EditTaskForm taskId={task.id} />)
    expect((await screen.findByRole('alert')).textContent).toContain('HTTP 404')
    expect(screen.getByRole('button', { name: 'Sačuvaj izmene' })).toBeTruthy()
    expect(screen.getByLabelText('Naslov zadatka')).toBeTruthy()
  })

  it('ne šalje PUT zahtev kada GET učitavanje zadatka nije uspelo', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 404 }))
    vi.stubGlobal('fetch', fetchMock)
    render(<EditTaskForm taskId={task.id} />)
    await screen.findByRole('alert')
    fireEvent.click(screen.getByRole('button', { name: 'Sačuvaj izmene' }))
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(fetchMock.mock.calls[0][0]).toBe('/api/authoring/tasks/task-42')
  })
})
