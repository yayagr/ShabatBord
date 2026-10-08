import type { Assignment, CalendarYear, Shabbat } from './types'

export class ApiError extends Error {
  readonly status: number
  constructor(message: string, status: number) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function isAssignment(value: unknown): value is Assignment {
  return isObject(value) && typeof value.date === 'string'
    && typeof value.familyName === 'string' && typeof value.version === 'string'
}

function isShabbat(value: unknown): value is Shabbat {
  return isObject(value) && typeof value.date === 'string'
    && typeof value.hebrewDate === 'string' && typeof value.reading === 'string'
    && typeof value.isFestival === 'boolean' && typeof value.isMevarchim === 'boolean'
    && (value.blessedMonth === null || typeof value.blessedMonth === 'string')
    && Array.isArray(value.roshChodeshDates) && value.roshChodeshDates.every(date => typeof date === 'string')
    && (value.assignment === null || isAssignment(value.assignment))
}

async function request(url: string, options: RequestInit): Promise<unknown> {
  let response: Response
  try {
    response = await fetch(url, options)
  } catch (error) {
    if (error instanceof Error && error.name === 'AbortError') throw error
    throw new ApiError('לא ניתן להתחבר לשרת. בדקו את החיבור ונסו שוב.', 0)
  }
  if (response.status === 204) return null
  let body: unknown
  try {
    body = await response.json()
  } catch {
    throw new ApiError('השרת החזיר תשובה לא תקינה. נסו שוב או פנו למנהל המערכת.', response.status)
  }
  if (!response.ok) {
    const detail = isObject(body) && typeof body.detail === 'string'
      ? body.detail : 'הבקשה לא הושלמה. נסו שוב או פנו למנהל המערכת.'
    throw new ApiError(detail, response.status)
  }
  return body
}

export async function getCalendar(year: number, signal: AbortSignal): Promise<CalendarYear> {
  const data = await request(`/api/calendar?year=${year}`, { signal })
  if (!isObject(data) || typeof data.year !== 'number' || !Array.isArray(data.shabbatot)
    || !data.shabbatot.every(isShabbat))
    throw new ApiError('נתוני הלוח שהתקבלו אינם תקינים. נסו שוב.', 502)
  return { year: data.year, shabbatot: data.shabbatot }
}

export async function saveAssignment(date: string, familyName: string, version?: string): Promise<Assignment> {
  const data = await request(`/api/assignments/${date}`, {
    method: version ? 'PUT' : 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(version ? { familyName, version } : { familyName }),
  })
  if (!isAssignment(data))
    throw new ApiError('לא התקבל אישור תקין לשמירה. רעננו את הלוח לפני ניסיון נוסף.', 502)
  return data
}

export async function removeAssignment(assignment: Assignment): Promise<void> {
  await request(`/api/assignments/${assignment.date}?version=${encodeURIComponent(assignment.version)}`, {
    method: 'DELETE',
  })
}
