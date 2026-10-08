import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, getCalendar, saveAssignment } from './api'
import { gregorianDate } from './dates'

afterEach(() => vi.unstubAllGlobals())

describe('API failures and date-only display', () => {
  it('surfaces network failures', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')))
    await expect(getCalendar(2027, new AbortController().signal)).rejects.toThrow('לא ניתן להתחבר')
  })

  it('surfaces invalid JSON instead of pretending the calendar is empty', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('<html>error</html>')))
    await expect(getCalendar(2027, new AbortController().signal)).rejects.toBeInstanceOf(ApiError)
  })

  it('rejects incomplete response data', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{"year":2027,"shabbatot":[{}]}')))
    await expect(getCalendar(2027, new AbortController().signal)).rejects.toThrow('נתוני הלוח')
  })

  it('does not retry uncertain mutations', async () => {
    const mock = vi.fn().mockRejectedValue(new TypeError('lost connection'))
    vi.stubGlobal('fetch', mock)
    await expect(saveAssignment('2027-01-02', 'Cohen')).rejects.toBeInstanceOf(ApiError)
    expect(mock).toHaveBeenCalledTimes(1)
  })

  it('preserves the Gregorian date without timezone shifts', () => {
    expect(gregorianDate('2027-01-02')).toBe('02.01.2027')
  })
})
