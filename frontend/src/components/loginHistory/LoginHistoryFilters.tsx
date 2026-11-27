import { useId } from 'react'
import type { FormEvent } from 'react'
import { LOGIN_HISTORY_PAGE_SIZES } from '../../api/loginHistory'
import type { LoginHistoryFormValues } from '../../types/loginHistory'
// IDN-22 TIM 09

type Props = {
  values: LoginHistoryFormValues
  onChange: (values: LoginHistoryFormValues) => void
  onSubmit: (hasIncompleteDate: boolean) => void
  onReset: () => void
}

const OUTCOME_OPTIONS: { value: LoginHistoryFormValues['outcome']; label: string }[] = [
  { value: 'all', label: 'Sve' },
  { value: 'succeeded', label: 'Uspešne' },
  { value: 'failed', label: 'Neuspešne' },
]

/** IDN-22: keyboard-accessible filter form; Enter in any field submits through the submit button. */
export default function LoginHistoryFilters({ values, onChange, onSubmit, onReset }: Props) {
  const id = useId()

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    // A partially typed date has value "" but validity.badInput, so it must not silently become "no bound".
    const dateInputs = event.currentTarget.querySelectorAll<HTMLInputElement>('input[type="date"]')
    onSubmit(Array.from(dateInputs).some(input => input.validity.badInput))
  }

  return <form role="search" aria-label="Filter istorije prijava" className="login-history-filters" onSubmit={handleSubmit} noValidate>
    <div className="login-history-field">
      <label htmlFor={`${id}-from`}>Od</label>
      <input id={`${id}-from`} type="date" value={values.from} onChange={event => onChange({ ...values, from: event.target.value })} />
    </div>
    <div className="login-history-field">
      <label htmlFor={`${id}-to`}>Do</label>
      <input id={`${id}-to`} type="date" value={values.to} onChange={event => onChange({ ...values, to: event.target.value })} />
    </div>
    <fieldset className="login-history-outcome">
      <legend>Ishod</legend>
      {OUTCOME_OPTIONS.map(option => <label key={option.value}>
        <input
          type="radio"
          name={`${id}-outcome`}
          value={option.value}
          checked={values.outcome === option.value}
          onChange={() => onChange({ ...values, outcome: option.value })}
        />
        {option.label}
      </label>)}
    </fieldset>
    <div className="login-history-field">
      <label htmlFor={`${id}-page-size`}>Broj prijava po strani</label>
      <select id={`${id}-page-size`} value={values.pageSize} onChange={event => onChange({ ...values, pageSize: Number(event.target.value) })}>
        {LOGIN_HISTORY_PAGE_SIZES.map(size => <option key={size} value={size}>{size}</option>)}
      </select>
    </div>
    <div className="login-history-actions">
      <button type="submit">Prikaži</button>
      <button type="button" className="secondary" onClick={onReset}>Poništi</button>
    </div>
  </form>
}
