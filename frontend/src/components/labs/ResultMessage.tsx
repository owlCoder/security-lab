export type ResultTone = 'ok' | 'info' | 'error'

interface Props {
  tone: ResultTone
  lines: string[]
  status?: number
  requestId?: string
}

const TITLES: Record<ResultTone, string> = {
  ok: 'Uspeh',
  info: 'Prava su proverena',
  error: 'Zahtev nije prošao',
}

// Uspeh i obaveštenje se najavljuju kao status, greška kao alert, da je čitač ekrana pročita odmah.
// Svaki red je običan tekst u JSX-u, pa se HTML iz poruke nikad ne izvršava.
export default function ResultMessage({ tone, lines, status, requestId }: Props) {
  return (
    <div role={tone === 'error' ? 'alert' : 'status'} className={`labs-result ${tone}`}>
      <p className="labs-result-head">
        <span>{TITLES[tone]}</span>
        {status !== undefined && <span className="labs-code">HTTP {status}</span>}
      </p>
      {lines.map(line => <p key={line}>{line}</p>)}
      {requestId && <p className="labs-request-id">ID zahteva: {requestId}</p>}
    </div>
  )
}
