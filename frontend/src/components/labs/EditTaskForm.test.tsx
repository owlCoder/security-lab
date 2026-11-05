import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EditTaskForm } from './EditTaskForm'

const task = { id: 'task-42', title: 'Početni naslov', description: 'Početni opis' }

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
    fireEvent.click(screen.getByRole('button', { name: 'Sačuvaj izmene' }))
    expect((await screen.findByRole('status')).textContent).toContain('uspešno sačuvane')
    const [, options] = fetchMock.mock.calls[1]
    expect(options.method).toBe('PUT')
    expect(JSON.parse(options.body)).toEqual({ title: 'Novi naslov', description: task.description })
    expect(fetchMock.mock.calls[1][0]).toBe('/api/authoring/tasks/task-42')
  })

  it('ne prikazuje lažne podatke kada zadatak ne može da se učita', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 404 })))
    render(<EditTaskForm taskId={task.id} />)
    expect((await screen.findByRole('alert')).textContent).toContain('HTTP 404')
    expect(screen.queryByRole('button', { name: 'Sačuvaj izmene' })).toBeNull()
  })
})
