// API za preduslove laboratorije (LAB-06).
// Ekran samo šalje zahteve i prikazuje odgovor servera.
// Server (Osobe 1, 8 i 9) odlučuje o pravima, duplikatima i ciklusima.

export type ApiError = {
  status: number;
  code: string;
  message: string;
};

export type ApiResult<T> =
  | { ok: true; data: T }
  | { ok: false; error: ApiError };

export type PrerequisiteResponse = {
  laboratoryId: string;
  requiredLaboratoryId: string;
};

const BASE = '/api/laboratories';

// Poruke za kodove koje vraća server (kontroler Osobe 1).
const MESSAGES: Record<string, string> = {
  forbidden: 'Nemate pravo da menjate preduslove ove laboratorije.',
  duplicate_prerequisite: 'Ova laboratorija je već preduslov.',
  prerequisite_cycle: 'Ovaj preduslov bi napravio ciklus u putu učenja.',
  invalid_prerequisite:
    'Preduslov nije ispravan. Laboratorija ne može biti sopstveni preduslov.',
  invalid_laboratory_id: 'ID laboratorije nije ispravan.',
};

function messageFor(status: number, code: string): string {
  if (MESSAGES[code]) return MESSAGES[code];
  if (status === 401) return 'Prijavite se ponovo.';
  if (status === 403) return MESSAGES.forbidden;
  if (status === 404) return 'Laboratorija nije pronađena.';
  if (status === 400) return 'Uneti ID laboratorije nije ispravan.';
  return `Server je odbio zahtev (${status}).`;
}

async function request<T>(
  url: string,
  init?: RequestInit,
): Promise<ApiResult<T>> {
  let response: Response;
  try {
    response = await fetch(url, {
      credentials: 'include',
      headers: { 'Content-Type': 'application/json' },
      ...init,
    });
  } catch {
    return {
      ok: false,
      error: {
        status: 0,
        code: 'network_error',
        message: 'Server nije dostupan. Pokušajte ponovo.',
      },
    };
  }

  if (!response.ok) {
    let code = 'unknown_error';
    try {
      const body = (await response.json()) as { code?: string };
      if (body?.code) code = body.code;
    } catch {
      // server nije vratio JSON
    }
    return {
      ok: false,
      error: {
        status: response.status,
        code,
        message: messageFor(response.status, code),
      },
    };
  }

  if (response.status === 204) {
    return { ok: true, data: undefined as T };
  }
  return { ok: true, data: (await response.json()) as T };
}

// Server vraća listu ID-jeva direktnih preduslova.
export function getPrerequisites(
  laboratoryId: string,
): Promise<ApiResult<string[]>> {
  return request<string[]>(
    `${BASE}/${encodeURIComponent(laboratoryId)}/prerequisites`,
  );
}

export function addPrerequisite(
  laboratoryId: string,
  requiredLaboratoryId: string,
): Promise<ApiResult<PrerequisiteResponse>> {
  return request<PrerequisiteResponse>(
    `${BASE}/${encodeURIComponent(laboratoryId)}/prerequisites`,
    {
      method: 'POST',
      body: JSON.stringify({ requiredLaboratoryId }),
    },
  );
}

export function removePrerequisite(
  laboratoryId: string,
  requiredLaboratoryId: string,
): Promise<ApiResult<void>> {
  return request<void>(
    `${BASE}/${encodeURIComponent(laboratoryId)}/prerequisites/${encodeURIComponent(requiredLaboratoryId)}`,
    { method: 'DELETE' },
  );
}