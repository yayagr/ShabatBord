export interface Assignment {
  date: string
  familyName: string
  version: string
}

export interface Shabbat {
  date: string
  hebrewDate: string
  reading: string
  isFestival: boolean
  isMevarchim: boolean
  blessedMonth: string | null
  roshChodeshDates: string[]
  assignment: Assignment | null
}

export interface CalendarYear {
  year: number
  shabbatot: Shabbat[]
}
