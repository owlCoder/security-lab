import type { ApiFailure } from '../../api/labs'

// Poruke su naše i fiksne: ne prikazuju sadržaj tuđih podataka ni interne detalje servera (OPS-07).
export function failureLines(failure: ApiFailure): string[] {
  switch (failure.kind) {
    case 'invalid':
      return failure.messages
    case 'unauthenticated':
      return ['Niste prijavljeni. Izaberite test korisnika.']
    case 'forbidden':
      return ['Nemate dozvolu za ovu radnju. Server je odbio zahtev.']
    case 'notFound':
      return ['Laboratorija nije pronađena.']
    case 'conflict':
      return ['Isti podatak već postoji.']
    case 'notImplemented':
      return ['Server je proverio prava, ali kreiranje još nije implementirano (LAB-01 API je u izradi).']
    case 'filtersUnavailable':
      return ['Filteri po osobinama trenutno nisu dostupni. Pokušajte ponovo kasnije ili pretražite po nazivu.']
    default:
      return ['Došlo je do greške. Pokušajte ponovo.']
  }
}

const STATUS: Partial<Record<ApiFailure['kind'], number>> = {
  invalid: 400,
  unauthenticated: 401,
  forbidden: 403,
  notFound: 404,
  conflict: 409,
  notImplemented: 501,
  filtersUnavailable: 503,
}

/** HTTP status koji je server vratio, da se na demonstraciji vidi da je odluku doneo server. */
export function statusOf(failure: ApiFailure): number | undefined {
  return STATUS[failure.kind]
}

export function requestIdOf(failure: ApiFailure): string | undefined {
  return 'requestId' in failure ? failure.requestId : undefined
}
