import { useEffect, useState, type FormEvent } from 'react'
import { createLab, type TestUser } from '../../api/labs'
import {
    loadLabMetadata,
    renameLabTitle,
    saveLabMetadata,
    type MetadataFieldErrors,
} from '../../api/labMetadata'
import './TeacherMetadataForm.css'

interface TeacherMetadataFormProps {
    user: TestUser
    labId?: string
}

type TeacherFormFieldErrors = MetadataFieldErrors & { title?: string[] }

export default function TeacherMetadataForm({
    user,
    labId,
}: TeacherMetadataFormProps) {
    const [title, setTitle] = useState('')
    const [loadedTitle, setLoadedTitle] = useState('')
    const [objective, setObjective] = useState('')
    const [difficulty, setDifficulty] = useState('')
    const [type, setType] = useState('')
    const [tagsInput, setTagsInput] = useState('')
    const [fieldErrors, setFieldErrors] = useState<TeacherFormFieldErrors>({})
    const [formMessage, setFormMessage] = useState('')
    const [isSubmitting, setIsSubmitting] = useState(false)
    const [createdLabId, setCreatedLabId] = useState<string | null>(null)
    const [isLoadingExisting, setIsLoadingExisting] = useState(Boolean(labId))
    const [loadFailed, setLoadFailed] = useState(false)
    const [metadataSaved, setMetadataSaved] = useState(false)

    const tags = tagsInput
        .split(',')
        .map((tag) => tag.trim())
        .filter((tag) => tag.length > 0)

    useEffect(() => {
        if (!labId) return

        let cancelled = false
        setIsLoadingExisting(true)
        setLoadFailed(false)

        loadLabMetadata(labId, user)
            .then((result) => {
                if (cancelled) return

                if (result.kind === 'error') {
                    setLoadFailed(true)
                    setFormMessage('Nije moguće učitati postojeće podatke.')
                    return
                }

                setTitle(result.metadata.title)
                setLoadedTitle(result.metadata.title)
                setObjective(result.metadata.objective)
                setDifficulty(String(result.metadata.difficulty))
                setType(String(result.metadata.type))
                setTagsInput(result.metadata.tags.join(', '))
            })
            .finally(() => {
                if (!cancelled) setIsLoadingExisting(false)
            })

        return () => {
            cancelled = true
        }
    }, [labId, user])

    async function handleSubmit(event: FormEvent<HTMLFormElement>) {
        event.preventDefault()
        if (loadFailed) return

        setIsSubmitting(true)
        setFieldErrors({})
        setFormMessage('')

        try {
            let targetLabId = labId ?? createdLabId

            if (!targetLabId) {
                const created = await createLab(title, user)

                if (created.kind !== 'created') {
                    if (created.kind === 'invalid') {
                        setFieldErrors({ title: created.messages })
                        setFormMessage('Proveri označena polja.')
                    } else {
                        setFormMessage(
                            'Nije uspelo kreiranje laboratorije. Proveri prijavu i pokušaj ponovo.',
                        )
                    }
                    return
                }

                targetLabId = created.id
                setCreatedLabId(targetLabId)
            }

            if (labId && title !== loadedTitle) {
                const renameResult = await renameLabTitle(labId, title, user)

                if (renameResult.kind === 'invalid') {
                    setFieldErrors({ title: [renameResult.message] })
                    setFormMessage('Proveri označena polja.')
                    return
                }

                if (renameResult.kind === 'error') {
                    setFormMessage('Nije uspela izmena naziva laboratorije. Pokušaj ponovo.')
                    return
                }

                setLoadedTitle(title)
            }

            const result = await saveLabMetadata(
                targetLabId,
                {
                    objective,
                    difficulty: Number(difficulty),
                    type: Number(type),
                    tags,
                },
                user,
                labId || metadataSaved ? 'edit' : 'create',
            )

            if (result.kind === 'invalid') {
                setFieldErrors(result.fieldErrors)
                setFormMessage('Proveri označena polja.')
                return
            }

            if (result.kind === 'error') {
                setFormMessage('Nije uspelo čuvanje metapodataka. Pokušaj ponovo.')
                return
            }

            setMetadataSaved(true)
            setFormMessage('Laboratorija je sačuvana.')
        } finally {
            setIsSubmitting(false)
        }
    }

    return (
        <form
            className="teacher-metadata-form"
            aria-labelledby="teacher-metadata-heading"
            onSubmit={handleSubmit}>
            <h2 id="teacher-metadata-heading">
                {labId ? 'Izmeni laboratoriju' : 'Nova laboratorija'}
            </h2>
            {isLoadingExisting && <p role="status">Učitavam postojeće podatke...</p>}
            {formMessage && <p role="status">{formMessage}</p>}
            <div>
                <label htmlFor="lab-title">Naziv laboratorije</label>
                <input
                    id="lab-title"
                    type="text"
                    value={title}
                    maxLength={200}
                    required
                    disabled={Boolean(createdLabId)}
                    aria-invalid={Boolean(fieldErrors.title?.length)}
                    onChange={(event) => setTitle(event.target.value)}
                />
                {fieldErrors.title?.map((message, index) => (
                    <small key={index} role="alert">{message}</small>
                ))}
            </div>
            <div>
                <label htmlFor="lab-objective">Cilj laboratorije</label>
                <textarea
                    id="lab-objective"
                    value={objective}
                    maxLength={2000}
                    required
                    onChange={(event) => setObjective(event.target.value)}
                />
                {fieldErrors.objective?.map((message, index) => (
                    <small key={index} role="alert">{message}</small>
                ))}
            </div>
            <div>
                <label htmlFor="lab-difficulty">Težina</label>
                <select
                    id="lab-difficulty"
                    value={difficulty}
                    required
                    onChange={(event) => setDifficulty(event.target.value)}
                >
                    <option value="">Izaberi težinu</option>
                    <option value="0">Laka</option>
                    <option value="1">Srednja</option>
                    <option value="2">Teška</option>
                </select>
                {fieldErrors.difficulty?.map((message, index) => (
                    <small key={index} role="alert">{message}</small>
                ))}
            </div>
            <div>
                <label htmlFor="lab-type">Tip laboratorije</label>
                <select
                    id="lab-type"
                    value={type}
                    required
                    onChange={(event) => setType(event.target.value)}
                >
                    <option value="">Izaberi tip</option>
                    <option value="0">Kontrola pristupa</option>
                    <option value="1">Analiza logova</option>
                    <option value="2">Bezbednosna konfiguracija</option>
                    <option value="3">Modelovanje pretnji</option>
                    <option value="4">MFA i sesije</option>
                    <option value="5">Kontekstualna autorizacija</option>
                    <option value="6">Odgovor na incidente</option>
                    <option value="7">Izolovana usluga</option>
                </select>
                {fieldErrors.type?.map((message, index) => (
                    <small key={index} role="alert">{message}</small>
                ))}
            </div>
            <div>
                <label htmlFor="lab-tags">Oznake</label>
                <input
                    id="lab-tags"
                    type="text"
                    value={tagsInput}
                    onChange={(event) => setTagsInput(event.target.value)}
                    aria-describedby="lab-tags-help"
                />
                <small id="lab-tags-help">Odvoji oznake zarezima.</small>
                {fieldErrors.tags?.map((message, index) => (
                    <small key={index} role="alert">{message}</small>
                ))}
            </div>
            <button
                type="submit"
                disabled={isSubmitting || isLoadingExisting || loadFailed}
            >
                {isSubmitting ? 'Čuvanje...' : 'Sačuvaj laboratoriju'}
            </button>
        </form>
    )
}


