import { useEffect, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import AssignmentDialog from './AssignmentDialog'
import type { DialogSelection } from './AssignmentDialog'
import { getCalendar } from './api'
import { gregorianDate, israelToday, MAX_YEAR, MIN_YEAR } from './dates'
import { CalendarIcon, Chevron } from './Icon'
import type { CalendarYear } from './types'

type Filter = 'all' | 'mevarchim' | 'unassigned'
const years = Array.from({ length: MAX_YEAR - MIN_YEAR + 1 }, (_, index) => MIN_YEAR + index)

export default function CalendarPage() {
  const [params, setParams] = useSearchParams()
  const [today] = useState(israelToday)
  const rawYear = params.get('year')
  const parsedYear = rawYear === null ? Number(today.slice(0, 4)) : Number(rawYear)
  const yearValid = Number.isInteger(parsedYear) && parsedYear >= MIN_YEAR && parsedYear <= MAX_YEAR
  const year = yearValid ? parsedYear : Number(today.slice(0, 4))
  const [filter, setFilter] = useState<Filter>('all')
  const [calendar, setCalendar] = useState<CalendarYear | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [revision, setRevision] = useState(0)
  const [selection, setSelection] = useState<DialogSelection | null>(null)
  const [notice, setNotice] = useState('')
  const focusAfterRefresh = useRef<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    let active = true
    getCalendar(year, controller.signal).then(data => {
      if (active) { setCalendar(data); setError(''); setLoading(false) }
    }).catch(failure => {
      if (!active || controller.signal.aborted) return
      setError(failure instanceof Error ? failure.message : 'לא ניתן לטעון את לוח השבתות.')
      setLoading(false)
    })
    return () => { active = false; controller.abort() }
  }, [year, revision])

  useEffect(() => {
    if (!loading && focusAfterRefresh.current) {
      const date = focusAfterRefresh.current
      focusAfterRefresh.current = null
      const target = document.querySelector<HTMLButtonElement>(`[data-assignment-date="${date}"]`)
      const focusTarget = target ?? document.getElementById('filter')
      focusTarget?.focus()
    }
  }, [calendar, loading])

  function refresh() {
    setLoading(true)
    setError('')
    setRevision(value => value + 1)
  }

  function changeYear(next: number) {
    if (next === year && yearValid) return
    setCalendar(null)
    setLoading(true)
    setError('')
    setNotice('')
    setSelection(null)
    setParams({ year: String(next) })
  }

  const rows = calendar?.year === year ? calendar.shabbatot : []
  const assigned = rows.filter(row => row.assignment).length
  const mevarchim = rows.filter(row => row.isMevarchim).length
  const visible = rows.filter(row => filter === 'all'
    || (filter === 'mevarchim' && row.isMevarchim)
    || (filter === 'unassigned' && !row.assignment))
  const upcoming = rows.find(row => row.date >= today)?.date

  return (
    <main id="main" className="page" tabIndex={-1}>
      <section className="hero" aria-labelledby="page-title">
        <div className="hero-copy">
          <p className="eyebrow"><span className="small-line" /> שבתות של קהילה</p>
          <h1 id="page-title">כל שבת,<br className="mobile-break" /> <span>המשפחה שלה.</span></h1>
          <p className="hero-description">בוחרים שבת, מזינים את שם המשפחה, ומפנים מקום למפגש הבא.</p>
        </div>
        <div className="hero-note" aria-hidden="true">
          <CalendarIcon />
          <span>מתחילים בבראשית.<br /><strong>הרבה רגעים ביחד.</strong></span>
          <span className="note-star">✦</span>
        </div>
      </section>

      <div className="calendar-toolbar">
        <div className="year-navigation">
          <button className="icon-button" aria-label="המחזור הקודם" onClick={() => changeYear(year - 1)} disabled={year <= MIN_YEAR}><Chevron direction="right" /></button>
          <div className="year-field">
            <label htmlFor="year">מחזור המתחיל בשנת</label>
            <select id="year" value={year} onChange={event => changeYear(Number(event.target.value))}>
              {years.map(value => <option key={value} value={value}>{value}</option>)}
            </select>
          </div>
          <button className="icon-button" aria-label="המחזור הבא" onClick={() => changeYear(year + 1)} disabled={year >= MAX_YEAR}><Chevron direction="left" /></button>
        </div>
        <div className="filter-field">
          <label htmlFor="filter">הצגת שבתות</label>
          <select id="filter" value={filter} onChange={event => {
            const value = event.target.value
            if (value === 'all' || value === 'mevarchim' || value === 'unassigned') setFilter(value)
          }}>
            <option value="all">כל השבתות</option>
            <option value="mevarchim">שבת מברכים בלבד</option>
            <option value="unassigned">שבתות פנויות</option>
          </select>
        </div>
      </div>
      {!yearValid && <p className="inline-error" role="alert">השנה בכתובת אינה תקינה. בחרו שנה לועזית בין 1900 ל־2100.</p>}
      <p className="live-notice" role="status" aria-live="polite">{notice}</p>

      <section className="stats" aria-label="סיכום המחזור">
        <div className="stat-card"><span className="stat-label">שבתות במחזור</span><strong>{rows.length || '—'}</strong><span className="stat-detail">הזדמנויות להיות ביחד</span></div>
        <div className="stat-card"><span className="stat-label">שבתות משובצות</span><strong>{rows.length ? assigned : '—'}</strong><span className="stat-detail"><span className="status-dot" /> משפחות שכבר הצטרפו</span></div>
        <div className="stat-card accent-card"><span className="stat-label">שבתות פנויות</span><strong>{rows.length ? rows.length - assigned : '—'}</strong><span className="stat-detail">אולי אחת מהן תהיה שלכם?</span></div>
      </section>

      <section className="calendar-panel" aria-labelledby="table-title" aria-busy={loading}>
        <div className="panel-heading">
          <div>
            <h2 id="table-title">מחזור שבתות <bdi>{year}–{year + 1}</bdi></h2>
            <p>{loading && rows.length > 0 ? 'מעדכנים את השיבוצים…' : 'מבראשית ועד השבת שלפני בראשית הבאה'}</p>
            {rows.length > 0 && (
              <p className="cycle-range" aria-label="טווח תאריכי המחזור">
                <time dateTime={rows[0].date}><bdi>{gregorianDate(rows[0].date)}</bdi></time>
                {' – '}
                <time dateTime={rows[rows.length - 1].date}><bdi>{gregorianDate(rows[rows.length - 1].date)}</bdi></time>
              </p>
            )}
          </div>
          <span className="legend"><span className="legend-mark" /> שבת מברכים <span className="legend-count">{mevarchim || '—'}</span></span>
        </div>
        {error ? (
          <div className="state-container"><CalendarIcon /><h3>הלוח לא נטען</h3><p role="alert">{error}</p><button className="button primary" onClick={refresh}>ניסיון נוסף</button></div>
        ) : loading && rows.length === 0 ? (
          <div className="state-container loading-state"><span className="spinner" /><p>טוענים את שבתות המחזור…</p></div>
        ) : visible.length === 0 ? (
          <div className="state-container"><CalendarIcon /><h3>{rows.length ? 'אין שבתות בסינון שבחרתם' : 'לא נמצאו שבתות במחזור זה'}</h3><p>אפשר לבחור סינון אחר כדי לראות את הלוח המלא.</p></div>
        ) : (
          <div className="table-scroll">
            <table className="shabbat-table">
              <caption className="sr-only">מחזור שבתות המתחיל בבראשית בשנת {year}, תאריכים, פרשות ושיבוצי משפחות</caption>
              <thead><tr><th scope="col">תאריך</th><th scope="col">פרשה / קריאת חג</th><th scope="col">סוג השבת</th><th scope="col">המשפחה</th><th scope="col">בחירה ושיבוץ</th></tr></thead>
              <tbody>
                {visible.map(row => (
                  <tr key={row.date} className={row.isMevarchim ? 'mevarchim-row' : undefined}>
                    <th scope="row" data-label="תאריך" className="date-cell">
                      <span className="gregorian"><time dateTime={row.date}><bdi>{gregorianDate(row.date)}</bdi></time></span>
                      <span className="secondary-text">{row.hebrewDate}</span>
                      {row.date === upcoming && <span className="upcoming-label">השבת הקרובה בלוח</span>}
                    </th>
                    <td data-label="פרשה / קריאת חג"><span className="reading">{row.reading}</span>{row.isFestival && <span className="secondary-text">קריאה מיוחדת לחג</span>}</td>
                    <td data-label="סוג השבת">
                      {row.isMevarchim ? <><span className="mevarchim-badge">✦ שבת מברכים</span><span className="secondary-text">מברכים חודש {row.blessedMonth}</span><span className="rosh-dates">ראש חודש: {row.roshChodeshDates.map(date => gregorianDate(date)).join(' · ')}</span></>
                        : <span className="ordinary-shabbat">{row.isFestival ? 'שבת וחג' : 'שבת'}</span>}
                    </td>
                    <td data-label="המשפחה">{row.assignment ? <span className="family-name"><span className="family-dot" /><bdi>{row.assignment.familyName}</bdi></span>
                      : <span className="unassigned"><span className="empty-dot" /> פנויה לבחירה</span>}</td>
                    <td data-label="בחירה ושיבוץ" className="actions-cell">
                      {row.assignment ? <div className="row-actions"><button className="text-button" data-assignment-date={row.date} aria-label={`עריכת השיבוץ ל־${gregorianDate(row.date)}`} onClick={() => setSelection({ shabbat: row, mode: 'assign' })}>עריכה</button><span className="action-divider" /><button className="text-button remove-button" aria-label={`הסרת השיבוץ ל־${gregorianDate(row.date)}`} onClick={() => setSelection({ shabbat: row, mode: 'remove' })}>הסרה</button></div>
                        : <button className="choose-button" data-assignment-date={row.date} aria-label={`בחרו שבת — ${gregorianDate(row.date)}`} onClick={() => setSelection({ shabbat: row, mode: 'assign' })}><span aria-hidden="true">＋</span> בחרו שבת</button>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {rows.length > 0 && !error && <div className="panel-footer"><span>{visible.length} מתוך {rows.length} שבתות</span><span>שם אחד לכל שבת. משפחה אחת, הרבה שבתות.</span><button className="text-button" onClick={refresh} disabled={loading}>רענון הלוח</button></div>}
      </section>
      <p className="community-note"><span aria-hidden="true">✦</span> שבת מברכים היא השבת שלפני ראש חודש. לפני תשרי אין ברכת החודש.</p>
      {selection && <AssignmentDialog selection={selection} onClose={() => setSelection(null)}
        onConflict={refresh} onSaved={message => {
          focusAfterRefresh.current = selection.shabbat.date
          setSelection(null); setNotice(message); refresh()
        }} />}
    </main>
  )
}
