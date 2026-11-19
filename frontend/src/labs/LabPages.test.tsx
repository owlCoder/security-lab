import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { session, setFetcher } from '../api/http'
import StudentLabPage from './StudentLabPage'
import LabPreviewPage from './LabPreviewPage'

const LAB_ID = '44444444-4444-4444-4444-444444444444'
const SECRET = 'SECRET_ANSWER_TEST_92841'

const publicView = {
  labId: LAB_ID,
  title: 'Test laboratorija',
  goal: 'Javni cilj laboratorije',
  tasks: [
    {
      taskId: 'task-1',
      order: 1,
      prompt: 'Javno pitanje',
      options: ['Opcija A', 'Opcija B'],
      expectedAnswer: SECRET,
    },
  ],
  expectedAnswer: SECRET,
}

afterEach(() => {
  cleanup()
  session.set(null)
  setFetcher((input, init) => fetch(input, init))
})

const pages = [
  {
    name: 'Studentski prikaz',
    Page: StudentLabPage,
    user: 'student' as const,
    preview: false,
  },
  {
    name: 'Pregled pre objave',
    Page: LabPreviewPage,
    user: 'teacher' as const,
    preview: true,
  },
]

describe.each(pages)('$name', ({ Page, user, preview }) => {
  it('prikazuje javni sadržaj i izostavlja expectedAnswer iz odgovora', async () => {
    const responseBody = preview
      ? { isPreview: true, state: 'Draft', view: publicView }
      : publicView

    setFetcher(async () => {
      return new Response(JSON.stringify(responseBody), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    })

    const { container } = render(
      <Page labId={LAB_ID} user={user} />,
    )

    await screen.findByRole('heading', {
      name: 'Test laboratorija',
    })

    expect(screen.getByText('Javni cilj laboratorije')).toBeTruthy()
    expect(screen.getByText('Javno pitanje')).toBeTruthy()
    expect(screen.getByText('Opcija A')).toBeTruthy()
    expect(screen.getByText('Opcija B')).toBeTruthy()

    expect(container.innerHTML).not.toContain(SECRET)
    expect(container.innerHTML).not.toContain('expectedAnswer')

    if (preview) {
      expect(screen.getByText('Pregled kao student')).toBeTruthy()
    }
  })

  it('jednako prikazuje odbijen pristup i nepostojeću laboratoriju', async () => {
    const renderedResults: string[] = []

    for (const status of [401, 403, 404]) {
      setFetcher(async () => {
        return new Response(
          JSON.stringify({
            code: `internal_code_${status}`,
            message: `Interni detalj za status ${status}`,
          }),
          {
            status,
            headers: { 'Content-Type': 'application/json' },
          },
        )
      })

      const { container, unmount } = render(
        <Page labId={LAB_ID} user={user} />,
      )

      const alert = await screen.findByRole('alert')

      expect(alert.textContent).toBe('Laboratorija nije dostupna')
      expect(container.textContent).not.toContain('Interni detalj')
      expect(
        screen.queryByRole('heading', { name: 'Test laboratorija' }),
      ).toBeNull()

      renderedResults.push(container.innerHTML)
      unmount()
    }

    expect(new Set(renderedResults).size).toBe(1)
  })
})