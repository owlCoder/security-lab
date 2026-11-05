// FAKE – zameniće se pravim LAB-07 endpointom (Platform.LabLocalization preko Platform.Api).
//
// Oponaša planirano ponašanje servera, kako je opisano u docs/components/LAB-07-localization.md:
// - svi jezici iste laboratorije vraćaju ISTI revisionId i ISTI evaluator, menja se samo tekst;
// - ako prevod ne postoji, vraća se podrazumevani srpski (sr);
// - nepodržan jezik -> 400 INVALID_LANGUAGE, nepostojeća laboratorija -> 404 bez detalja;
// - bez korisnika -> 401, kao LAB-01 kostur.
// Ovo nije bezbednosna provera. Prava proverava server; fake samo vraća iste statuse
// da bi ekran mogao da se razvija i testira pre nego što endpoint postoji.

import type { TestUser } from './labs'

type Translation = { title: string; description: string; instructions: string }

interface FakeLab {
  revisionId: string
  evaluatorVersion: string
  translations: Partial<Record<'sr' | 'en', Translation>>
}

export const DEMO_LAB_ID = '7d3f1c2a-5b8e-4c1d-9a6f-2e4b8c0d1a11'
/** Laboratorija čiji tekst sadrži HTML i skriptu: služi da se pokaže da se tekst prikazuje kao tekst (SEC-05). */
export const DEMO_XSS_LAB_ID = '7d3f1c2a-5b8e-4c1d-9a6f-2e4b8c0d1a22'

export const XSS_TITLE = '<script>alert(1)</script>'

const LABS: Record<string, FakeLab> = {
  [DEMO_LAB_ID]: {
    revisionId: 'b2c4e6a8-1d3f-4a5b-8c7d-9e0f1a2b3c01',
    evaluatorVersion: 'config-check 1.0',
    translations: {
      sr: {
        title: 'Bezbedna konfiguracija veb servera',
        description: 'Pronađite i ispravite nebezbedna podešavanja u konfiguraciji veb servera.',
        instructions: '1. Otvorite priloženu konfiguraciju.\n2. Označite podešavanja koja otkrivaju interne podatke.\n3. Predložite bezbednu vrednost za svako.',
      },
      en: {
        title: 'Secure web server configuration',
        description: 'Find and fix insecure settings in a web server configuration.',
        instructions: '1. Open the attached configuration.\n2. Mark the settings that expose internal data.\n3. Suggest a safe value for each one.',
      },
    },
  },
  [DEMO_XSS_LAB_ID]: {
    revisionId: 'b2c4e6a8-1d3f-4a5b-8c7d-9e0f1a2b3c02',
    evaluatorVersion: 'event-analysis 2.1',
    // Namerno samo srpski prevod: na en se vidi povratak na podrazumevani jezik.
    translations: {
      sr: {
        title: XSS_TITLE,
        description: '<img src="x" onerror="alert(1)"> Opis sa HTML oznakama mora da se vidi kao običan tekst.',
        instructions: '<b>Ovo nije podebljano</b>, jer se tekst nikad ne tumači kao HTML.',
      },
    },
  },
}

function json(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

export async function fakeLabContentResponse(labId: string, language: string, user: TestUser): Promise<Response> {
  if (!user) return json(401, { title: 'Unauthenticated' })

  const lang = language.trim().toLowerCase()
  if (lang !== '' && lang !== 'sr' && lang !== 'en') {
    return json(400, { title: 'Bad request', code: 'INVALID_LANGUAGE' })
  }

  const lab = LABS[labId.trim().toLowerCase()]
  const chosen = lang === 'en' ? 'en' : 'sr'
  const text = lab?.translations[chosen] ?? lab?.translations.sr
  if (!lab || !text) return json(404, { title: 'Not found' })

  return json(200, {
    revisionId: lab.revisionId,
    evaluatorVersion: lab.evaluatorVersion,
    language: lab.translations[chosen] ? chosen : 'sr',
    ...text,
  })
}
