import { LABS_BASE_PATH, TEST_USER_HEADER, type TestUser } from './labs'

export interface LabMetadataInput {
    objective: string
    difficulty: number
    type: number
    tags: string[]
}

export type MetadataFieldErrors = Partial<
    Record<'objective' | 'difficulty' | 'type' | 'tags', string[]>
>

export type SaveMetadataOutcome =
    | { kind: 'saved' }
    | { kind: 'invalid'; fieldErrors: MetadataFieldErrors }
    | { kind: 'error'; status?: number }

export type RenameLabTitleOutcome =
    | { kind: 'renamed' }
    | { kind: 'invalid'; message: string }
    | { kind: 'error'; status?: number }

export interface ExistingLabMetadata extends LabMetadataInput {
    title: string
}

export type LoadMetadataOutcome =
    | { kind: 'loaded'; metadata: ExistingLabMetadata }
    | { kind: 'error'; status?: number }

function requestHeaders(
    user: TestUser,
    withBody: boolean,
): Record<string, string> {
    const headers: Record<string, string> = {
        Accept: 'application/json',
    }

    if (withBody) {
        headers['Content-Type'] = 'application/json'
    }

    if (import.meta.env.DEV && user) {
        headers[TEST_USER_HEADER] = `${user.id}:${user.role}`
    }

    return headers
}

export async function saveLabMetadata(
    labId: string,
    metadata: LabMetadataInput,
    user: TestUser,
    mode: 'create' | 'edit',
): Promise<SaveMetadataOutcome> {
    try {
        const response = await fetch(
            `${LABS_BASE_PATH}/${encodeURIComponent(labId)}/metadata`,
            {
                method: mode === 'create' ? 'POST' : 'PATCH',
                headers: requestHeaders(user, true),
                credentials: 'same-origin',
                body: JSON.stringify(metadata),
            },
        )

        if (response.ok) {
            return { kind: 'saved' }
        }

        if (response.status === 400) {
            const body: unknown = await response.json().catch(() => null)

            if (body && typeof body === 'object' && 'errors' in body) {
                const errors = (body as { errors?: unknown }).errors

                if (errors && typeof errors === 'object') {
                    const fieldErrors: MetadataFieldErrors = {}

                    for (const [key, value] of Object.entries(errors)) {
                        const field = key.split(/[.[\]]/)[0].toLowerCase()

                        if (
                            (field === 'objective' ||
                                field === 'difficulty' ||
                                field === 'type' ||
                                field === 'tags') &&
                            Array.isArray(value)
                        ) {
                            fieldErrors[field] = value.filter(
                                (message): message is string =>
                                    typeof message === 'string',
                            )
                        }
                    }

                    return { kind: 'invalid', fieldErrors }
                }
            }

            return { kind: 'invalid', fieldErrors: {} }
        }

        return { kind: 'error', status: response.status }
    } catch {
        return { kind: 'error' }
    }
}

export async function renameLabTitle(
    labId: string,
    title: string,
    user: TestUser,
): Promise<RenameLabTitleOutcome> {
    try {
        const response = await fetch(
            `${LABS_BASE_PATH}/${encodeURIComponent(labId)}/title`,
            {
                method: 'PATCH',
                headers: requestHeaders(user, true),
                credentials: 'same-origin',
                body: JSON.stringify({ title }),
            },
        )

        if (response.ok) {
            return { kind: 'renamed' }
        }

        if (response.status === 400) {
            return {
                kind: 'invalid',
                message: 'Naziv je obavezan i može imati najviše 200 znakova.',
            }
        }

        return { kind: 'error', status: response.status }
    } catch {
        return { kind: 'error' }
    }
}

export async function loadLabMetadata(
    labId: string,
    user: TestUser,
): Promise<LoadMetadataOutcome> {
    try {
        const response = await fetch(
            `/api/catalog/${encodeURIComponent(labId)}`,
            {
                headers: requestHeaders(user, false),
                credentials: 'same-origin',
                cache: 'no-store',
            },
        )

        if (!response.ok) {
            return { kind: 'error', status: response.status }
        }

        const body: unknown = await response.json()

        if (!body || typeof body !== 'object') {
            return { kind: 'error' }
        }

        const data = body as Record<string, unknown>
        const { title, objective, difficulty, type, tags } = data

        if (
            typeof title !== 'string' ||
            typeof objective !== 'string' ||
            typeof difficulty !== 'number' ||
            typeof type !== 'number' ||
            !Array.isArray(tags) ||
            !tags.every((tag): tag is string => typeof tag === 'string')
        ) {
            return { kind: 'error' }
        }

        return {
            kind: 'loaded',
            metadata: { title, objective, difficulty, type, tags },
        }
    } catch {
        return { kind: 'error' }
    }
}
