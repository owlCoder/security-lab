import type { LoginHistoryItem } from '../../types/loginHistory'
import { LOGIN_HISTORY_MESSAGES, pageLabel } from './loginHistoryMessages'
// IDN-22 TIM 09

type Props = {
  items: LoginHistoryItem[]
  page: number
  pageSize: number
  loading: boolean
  focusRef: (element: HTMLElement | null) => void
  onPrevious: () => void
  onNext: () => void
}

function formatTime(value: string): string {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString('sr-Latn-RS')
}

/**
 * IDN-22: own login events as plain text. The address is rendered exactly as the server masked it (AUD-19);
 * outcome is a word, not only a color.
 */
export default function LoginHistoryTable({ items, page, pageSize, loading, focusRef, onPrevious, onNext }: Props) {
  return <div className="login-history-results">
    {items.length === 0
      ? <p className="login-history-empty" ref={focusRef} tabIndex={-1}>{LOGIN_HISTORY_MESSAGES.empty}</p>
      : <table className="login-history-table">
          <caption ref={focusRef} tabIndex={-1}>Vaše prijave</caption>
          <thead>
            <tr>
              <th scope="col">Vreme</th>
              <th scope="col">Ishod</th>
              <th scope="col">Adresa</th>
            </tr>
          </thead>
          <tbody>
            {items.map((item, index) => <tr key={`${item.occurredAt}-${index}`}>
              <td><time dateTime={item.occurredAt}>{formatTime(item.occurredAt)}</time></td>
              <td className={item.succeeded ? 'login-history-succeeded' : 'login-history-failed'}>
                {item.succeeded ? 'Uspešna' : 'Neuspešna'}
              </td>
              <td>{item.maskedSourceAddress}</td>
            </tr>)}
          </tbody>
        </table>}
    <nav className="login-history-paging" aria-label="Stranice istorije prijava">
      <button type="button" className="secondary" onClick={onPrevious} disabled={loading || page <= 1}>Prethodna</button>
      <span>{pageLabel(page)}</span>
      <button type="button" className="secondary" onClick={onNext} disabled={loading || items.length < pageSize}>Sledeća</button>
    </nav>
  </div>
}
