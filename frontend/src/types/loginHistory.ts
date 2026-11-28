// IDN-22 TIM 09
// Contract of GET /api/auth/login-history. There is deliberately no userId: the server derives the subject from the session (SEC-01).

export type LoginOutcomeFilter = 'succeeded' | 'failed'

export type LoginHistoryFilter = { from?: string; to?: string; outcome?: LoginOutcomeFilter; page: number; pageSize: number }

export type LoginHistoryItem = { occurredAt: string; succeeded: boolean; maskedSourceAddress: string }

export type LoginHistoryPage = { items: LoginHistoryItem[]; page: number; pageSize: number }

/** Raw filter form state; dates are local `YYYY-MM-DD` values from `<input type="date">`. Never stored in the URL or storage. */
export type LoginHistoryFormValues = { from: string; to: string; outcome: 'all' | LoginOutcomeFilter; pageSize: number }
