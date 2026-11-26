import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as labsModule from '../api/labs'
import { getLabContent, type LabContent } from '../api/labs'
import { fakeLabContentResponse } from '../api/fakeLabContent'

vi.mock('../api/fakeLabContent', () => ({
    fakeLabContentResponse: vi.fn(),
}))

function validPayload(steps: unknown): Record<string, unknown> {
    return {
        revisionId: 'rev-1',
        evaluatorVersion: '1.0',
        language: 'sr',
        title: 'Test laboratorija',
        description: 'Opis laboratorije',
        instructions: 'Glavno uputstvo',
        steps,
    }
}

function mockResponse(body: unknown, status = 200) {
    const response = new Response(JSON.stringify(body), {
        status,
        headers: { 'Content-Type': 'application/json' },
    })
    if (labsModule.USE_FAKE_LAB_CONTENT) {
        vi.mocked(fakeLabContentResponse).mockResolvedValueOnce(response)
    } else {
        vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(response)
    }
}

const mockedFakeLabContentResponse = vi.mocked(fakeLabContentResponse)

beforeEach(() => {
    mockedFakeLabContentResponse.mockReset()
})

describe('getLabContent - validacija i redosled koraka (steps)', () => {
    it('vraca validne korake sortirane po sequenceOrder', async () => {
        mockResponse(
            validPayload([
                { sequenceOrder: 2, title: 'Korak 2', instruction: 'Druga instrukcija' },
                { sequenceOrder: 1, title: 'Korak 1', instruction: 'Prva instrukcija' },
            ]),
        )

        const result = await getLabContent('lab-1', 'sr', null)

        expect(result.kind).toBe('ok')
        const content = (result as { kind: 'ok'; content: LabContent; usedFallback: boolean }).content
        expect(content.steps).toEqual([
            { sequenceOrder: 1, title: 'Korak 1', instruction: 'Prva instrukcija' },
            { sequenceOrder: 2, title: 'Korak 2', instruction: 'Druga instrukcija' },
        ])
    })

    it('prihvata izostavljen steps kao prazan niz', async () => {
        const payload = validPayload(undefined)
        delete payload.steps
        mockResponse(payload)

        const result = await getLabContent('lab-1', 'sr', null)

        expect(result.kind).toBe('ok')
        const content = (result as { kind: 'ok'; content: LabContent; usedFallback: boolean }).content
        expect(content.steps).toEqual([])
    })

    it('odbija kada steps nije niz', async () => {
        for (const invalidSteps of ['neispravno', 123, true, { sequenceOrder: 1 }]) {
            mockResponse(validPayload(invalidSteps))
            const result = await getLabContent('lab-1', 'sr', null)
            expect(result.kind).toBe('error')
        }
    })

    it('odbija kada element u steps nije objekat', async () => {
        for (const badItem of [null, 'korak', 42, []]) {
            mockResponse(validPayload([badItem]))
            const result = await getLabContent('lab-1', 'sr', null)
            expect(result.kind).toBe('error')
        }
    })

    it('odbija neispravan sequenceOrder (0, negativan, decimalan, pogresan tip)', async () => {
        for (const badOrder of [0, -1, -5, 1.5, '1', null]) {
            mockResponse(
                validPayload([{ sequenceOrder: badOrder, title: 'Korak 1', instruction: 'Instrukcija 1' }]),
            )
            const result = await getLabContent('lab-1', 'sr', null)
            expect(result.kind).toBe('error')
        }
    })

    it('odbija preskocene ili duplirane sequenceOrder vrednosti', async () => {
        mockResponse(
            validPayload([
                { sequenceOrder: 1, title: 'Korak 1', instruction: 'Instrukcija 1' },
                { sequenceOrder: 3, title: 'Korak 3', instruction: 'Instrukcija 3' },
            ]),
        )
        expect((await getLabContent('lab-1', 'sr', null)).kind).toBe('error')

        mockResponse(
            validPayload([
                { sequenceOrder: 1, title: 'Korak 1', instruction: 'Instrukcija 1' },
                { sequenceOrder: 1, title: 'Duplikat', instruction: 'Instrukcija 2' },
            ]),
        )
        expect((await getLabContent('lab-1', 'sr', null)).kind).toBe('error')
    })

    it('odbija kada nedostaju ili imaju pogresan tip title ili instruction', async () => {
        mockResponse(
            validPayload([{ sequenceOrder: 1, title: 99, instruction: 'Instrukcija 1' }]),
        )
        expect((await getLabContent('lab-1', 'sr', null)).kind).toBe('error')

        mockResponse(
            validPayload([{ sequenceOrder: 1, title: 'Korak 1', instruction: null }]),
        )
        expect((await getLabContent('lab-1', 'sr', null)).kind).toBe('error')

        mockResponse(
            validPayload([{ sequenceOrder: 1, title: 'Korak 1' }]),
        )
        expect((await getLabContent('lab-1', 'sr', null)).kind).toBe('error')
    })
})