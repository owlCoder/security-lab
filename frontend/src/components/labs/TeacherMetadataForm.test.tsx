import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import TeacherMetadataForm from './TeacherMetadataForm'

const teacher = { id: 'teacher-1', role: 'Teacher' } as const

afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
})

describe('TeacherMetadataForm', () => {
    it('šalje prvo naziv, pa metapodatke laboratorije', async () => {
        const fetchMock = vi.fn()
        fetchMock
            .mockResolvedValueOnce(
                new Response(JSON.stringify({ id: 'lab-123' }), { status: 201 }),
            )
            .mockResolvedValueOnce(new Response('{}', { status: 201 }))

        vi.stubGlobal('fetch', fetchMock)
        render(<TeacherMetadataForm user={teacher} />)

        fireEvent.change(screen.getByLabelText('Naziv laboratorije'), {
            target: { value: 'Moja laboratorija' },
        })
        fireEvent.change(screen.getByLabelText('Cilj laboratorije'), {
            target: { value: 'Naučiti osnove bezbednosti' },
        })
        fireEvent.change(screen.getByLabelText('Težina'), {
            target: { value: '1' },
        })
        fireEvent.change(screen.getByLabelText('Tip laboratorije'), {
            target: { value: '3' },
        })
        fireEvent.change(screen.getByLabelText('Oznake'), {
            target: { value: 'web, auth' },
        })

        fireEvent.submit(
            screen.getByRole('button', { name: 'Sačuvaj laboratoriju' }),
        )

        expect(await screen.findByText('Laboratorija je sačuvana.')).toBeTruthy()

        expect(fetchMock).toHaveBeenNthCalledWith(
            1,
            '/api/laboratories/',
            expect.objectContaining({
                method: 'POST',
                body: JSON.stringify({ title: 'Moja laboratorija' }),
            }),
        )

        expect(fetchMock).toHaveBeenNthCalledWith(
            2,
            '/api/laboratories/lab-123/metadata',
            expect.objectContaining({
                method: 'POST',
                body: JSON.stringify({
                    objective: 'Naučiti osnove bezbednosti',
                    difficulty: 1,
                    type: 3,
                    tags: ['web', 'auth'],
                }),
            }),
        )
    })

    it('prikazuje serversku grešku uz odgovarajuće polje', async () => {
        const fetchMock = vi.fn()
        fetchMock
            .mockResolvedValueOnce(
                new Response(JSON.stringify({ id: 'lab-123' }), { status: 201 }),
            )
            .mockResolvedValueOnce(
                new Response(
                    JSON.stringify({
                        errors: {
                            Objective: ['Cilj laboratorije nije dovoljno detaljan.'],
                        },
                    }),
                    { status: 400 },
                ),
            )

        vi.stubGlobal('fetch', fetchMock)
        render(<TeacherMetadataForm user={teacher} />)

        fireEvent.change(screen.getByLabelText('Naziv laboratorije'), {
            target: { value: 'Moja laboratorija' },
        })
        fireEvent.change(screen.getByLabelText('Cilj laboratorije'), {
            target: { value: 'Kratak cilj' },
        })
        fireEvent.change(screen.getByLabelText('Težina'), {
            target: { value: '1' },
        })
        fireEvent.change(screen.getByLabelText('Tip laboratorije'), {
            target: { value: '3' },
        })

        fireEvent.submit(
            screen.getByRole('button', { name: 'Sačuvaj laboratoriju' }),
        )

        expect(
            await screen.findByText('Cilj laboratorije nije dovoljno detaljan.'),
        ).toBeTruthy()
    })
    it('učitava postojeće podatke pri izmeni', async () => {
        const fetchMock = vi.fn().mockResolvedValue(
            new Response(
                JSON.stringify({
                    id: 'lab-123',
                    title: 'Stara laboratorija',
                    status: 'Draft',
                    objective: 'Postojeći cilj',
                    difficulty: 2,
                    type: 4,
                    tags: ['web', 'auth'],
                }),
                { status: 200 },
            ),
        )

        vi.stubGlobal('fetch', fetchMock)
        render(<TeacherMetadataForm user={teacher} labId="lab-123" />)

        expect(await screen.findByDisplayValue('Stara laboratorija')).toBeTruthy()
        expect(
            (screen.getByLabelText('Cilj laboratorije') as HTMLTextAreaElement).value,
        ).toBe('Postojeći cilj')
        expect(
            (screen.getByLabelText('Težina') as HTMLSelectElement).value,
        ).toBe('2')
        expect(
            (screen.getByLabelText('Tip laboratorije') as HTMLSelectElement).value,
        ).toBe('4')
        expect(
            (screen.getByLabelText('Oznake') as HTMLInputElement).value,
        ).toBe('web, auth')

        expect(fetchMock).toHaveBeenCalledWith(
            '/api/catalog/lab-123',
            expect.objectContaining({ cache: 'no-store' }),
        )
    })

    it('menja naziv preko title API-ja, pa šalje ostale metapodatke', async () => {
        const fetchMock = vi.fn()
        fetchMock
            .mockResolvedValueOnce(
                new Response(
                    JSON.stringify({
                        id: 'lab-123',
                        title: 'Stara laboratorija',
                        objective: 'Postojeći cilj',
                        difficulty: 2,
                        type: 4,
                        tags: ['web', 'auth'],
                    }),
                    { status: 200 },
                ),
            )
            .mockResolvedValueOnce(new Response('{}', { status: 200 }))
            .mockResolvedValueOnce(new Response('{}', { status: 200 }))

        vi.stubGlobal('fetch', fetchMock)
        render(<TeacherMetadataForm user={teacher} labId="lab-123" />)

        const titleInput = await screen.findByDisplayValue('Stara laboratorija')
        fireEvent.change(titleInput, { target: { value: 'Nova laboratorija' } })
        fireEvent.submit(
            screen.getByRole('button', { name: 'Sačuvaj laboratoriju' }),
        )

        expect(await screen.findByText('Laboratorija je sačuvana.')).toBeTruthy()
        expect(fetchMock).toHaveBeenNthCalledWith(
            2,
            '/api/laboratories/lab-123/title',
            expect.objectContaining({
                method: 'PATCH',
                body: JSON.stringify({ title: 'Nova laboratorija' }),
            }),
        )
        expect(fetchMock).toHaveBeenNthCalledWith(
            3,
            '/api/laboratories/lab-123/metadata',
            expect.objectContaining({
                method: 'PATCH',
                body: JSON.stringify({
                    objective: 'Postojeći cilj',
                    difficulty: 2,
                    type: 4,
                    tags: ['web', 'auth'],
                }),
            }),
        )
    })

    it('prikazuje serversku grešku za naziv uz polje naziva', async () => {
        const fetchMock = vi.fn()
        fetchMock
            .mockResolvedValueOnce(
                new Response(
                    JSON.stringify({
                        title: 'Stara laboratorija',
                        objective: 'Postojeći cilj',
                        difficulty: 2,
                        type: 4,
                        tags: ['web', 'auth'],
                    }),
                    { status: 200 },
                ),
            )
            .mockResolvedValueOnce(
                new Response(
                    JSON.stringify({
                        code: 'INVALID_TITLE',
                        message: 'Invalid title.',
                    }),
                    { status: 400 },
                ),
            )

        vi.stubGlobal('fetch', fetchMock)
        render(<TeacherMetadataForm user={teacher} labId="lab-123" />)

        const titleInput = await screen.findByDisplayValue('Stara laboratorija')
        fireEvent.change(titleInput, { target: { value: ' ' } })
        fireEvent.submit(
            screen.getByRole('button', { name: 'Sačuvaj laboratoriju' }),
        )

        expect(
            await screen.findByText(
                'Naziv je obavezan i može imati najviše 200 znakova.',
            ),
        ).toBeTruthy()
        expect(titleInput.getAttribute('aria-invalid')).toBe('true')
        expect(fetchMock).toHaveBeenCalledTimes(2)
    })
})
