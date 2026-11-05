import { useEffect, useState } from 'react'


export function useCountdown(until: number | null): number {
  const compute = () => (until == null ? 0 : Math.max(0, Math.ceil((until - Date.now()) / 1000)))
  const [remaining, setRemaining] = useState(compute)

  useEffect(() => {
    setRemaining(compute())
    if (until == null) return
    const id = setInterval(() => {
      const left = compute()
      setRemaining(left)
      if (left <= 0) clearInterval(id)
    }, 250)
    return () => clearInterval(id)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [until])

  return remaining
}

export function formatSeconds(total: number): string {
  if (total < 60) return `${total} s`
  const m = Math.floor(total / 60)
  const s = total % 60
  return s === 0 ? `${m} min` : `${m} min ${s} s`
}