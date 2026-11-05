import { describe, expect, it } from 'vitest'
import { DEFAULT_RETRY_AFTER_SECONDS, MAX_RETRY_AFTER_SECONDS, parseRetryAfter, toApiError } from '../api/http'

describe('parseRetryAfter (IDN-05)', () => {
  it('cita broj sekundi', () => expect(parseRetryAfter('30')).toBe(30))
  it('cita HTTP datum', () => {
    const now = new Date('2026-11-02T10:00:00Z')
    expect(parseRetryAfter('Mon, 02 Nov 2026 10:02:00 GMT', now)).toBe(120)
  })
  it('bez headera vraca podrazumevanu vrednost', () => {
    expect(parseRetryAfter(null)).toBe(DEFAULT_RETRY_AFTER_SECONDS)
    expect(parseRetryAfter('')).toBe(DEFAULT_RETRY_AFTER_SECONDS)
  })
  it('neispravan header vraca podrazumevanu vrednost', () => {
    expect(parseRetryAfter('abc')).toBe(DEFAULT_RETRY_AFTER_SECONDS)
    expect(parseRetryAfter('-5')).toBe(DEFAULT_RETRY_AFTER_SECONDS)
    expect(parseRetryAfter('1.5')).toBe(DEFAULT_RETRY_AFTER_SECONDS)
  })
  it('datum u proslosti i nula daju najmanje 1 s', () => {
    expect(parseRetryAfter('0')).toBe(1)
    expect(parseRetryAfter('Mon, 01 Jan 2001 00:00:00 GMT')).toBe(1)
  })
  it('ogranicava preveliku vrednost', () => {
    expect(parseRetryAfter('999999')).toBe(MAX_RETRY_AFTER_SECONDS)
  })
})

describe('toApiError', () => {
  it('429 dobija retryAfterSeconds iz headera', async () => {
    const res = new Response('{}', { status: 429, headers: { 'Retry-After': '45' } })
    const err = await toApiError(res)
    expect(err.status).toBe(429)
    expect(err.retryAfterSeconds).toBe(45)
  })
  it('telo koje nije JSON ne rusi obradu', async () => {
    const err = await toApiError(new Response('<html>', { status: 500 }))
    expect(err.status).toBe(500)
    expect(err.message).toBeTruthy()
  })
  it('koristi code i message iz tela', async () => {
    const res = new Response(JSON.stringify({ code: 'account_blocked', message: 'Nalog je blokiran.' }), { status: 403 })
    const err = await toApiError(res)
    expect(err).toMatchObject({ code: 'account_blocked', message: 'Nalog je blokiran.' })
  })
})