const dateFormat = new Intl.DateTimeFormat('he-IL', {
  day: '2-digit', month: '2-digit', year: 'numeric', timeZone: 'UTC',
})

export function gregorianDate(iso: string): string {
  return dateFormat.format(new Date(`${iso}T12:00:00Z`))
}

export function israelToday(): string {
  const parts = new Intl.DateTimeFormat('en', {
    timeZone: 'Asia/Jerusalem', year: 'numeric', month: '2-digit', day: '2-digit',
  }).formatToParts(new Date())
  const part = (type: Intl.DateTimeFormatPartTypes) => parts.find(item => item.type === type)!.value
  return `${part('year')}-${part('month')}-${part('day')}`
}

export const MIN_YEAR = 1900
export const MAX_YEAR = 2100
