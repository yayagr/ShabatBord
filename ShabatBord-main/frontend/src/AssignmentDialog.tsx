import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { ApiError, removeAssignment, saveAssignment } from './api'
import { gregorianDate } from './dates'
import type { Shabbat } from './types'

export type DialogSelection = { shabbat: Shabbat; mode: 'assign' | 'remove' }

interface Props {
  selection: DialogSelection
  onClose: () => void
  onSaved: (message: string) => void
  onConflict: () => void
}

export default function AssignmentDialog({ selection, onClose, onSaved, onConflict }: Props) {
  const { shabbat, mode } = selection
  const [name, setName] = useState(shabbat.assignment?.familyName ?? '')
  const [error, setError] = useState('')
  const [pending, setPending] = useState(false)
  const [conflict, setConflict] = useState(false)
  const dialogRef = useRef<HTMLDialogElement>(null)
  const inputRef = useRef<HTMLInputElement>(null)
  const titleRef = useRef<HTMLHeadingElement>(null)
  const removal = mode === 'remove'
  const title = removal ? 'הסרת שיבוץ' : shabbat.assignment ? 'עריכת השיבוץ' : 'השבת שלכם מתחילה כאן'

  useEffect(() => {
    const dialog = dialogRef.current!
    const previous = document.activeElement
    dialog.showModal()
    if (window.innerWidth >= 700 && inputRef.current) inputRef.current.focus()
    else titleRef.current?.focus()
    return () => {
      dialog.close()
      if (previous instanceof HTMLElement && previous.isConnected) previous.focus()
      else document.getElementById('filter')?.focus()
    }
  }, [])

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (pending || conflict) return
    if (!removal && !name.trim()) {
      setError('יש להזין את שם המשפחה.')
      return
    }
    setPending(true)
    setError('')
    try {
      if (removal && shabbat.assignment) {
        await removeAssignment(shabbat.assignment)
        onSaved('השיבוץ הוסר והשבת פנויה לבחירה.')
      } else {
        await saveAssignment(shabbat.date, name.trim(), shabbat.assignment?.version)
        onSaved('השיבוץ נשמר. תודה שהצטרפתם!')
      }
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : 'השינוי לא נשמר. נסו שוב.')
      if (failure instanceof ApiError && (failure.status === 409 || failure.status === 404)) {
        setConflict(true)
        onConflict()
      }
    } finally {
      setPending(false)
    }
  }

  return (
    <dialog ref={dialogRef} className="assignment-dialog" aria-labelledby="dialog-title"
      aria-describedby="dialog-description"
      onCancel={event => { event.preventDefault(); if (!pending) onClose() }}>
      <form onSubmit={submit}>
        <div className="dialog-topline">
          <span className="eyebrow">לוח השבתות הקהילתי</span>
          <button type="button" className="close-button" aria-label="סגירת החלונית" onClick={onClose} disabled={pending}>×</button>
        </div>
        <h2 id="dialog-title" ref={titleRef} tabIndex={-1}>{title}</h2>
        <p id="dialog-description">
          {removal ? `האם להסיר את השיבוץ של ${shabbat.assignment?.familyName}?` : 'מזינים שם משפחה ושומרים — זה כל מה שצריך.'}
        </p>
        <div className="dialog-date">
          <strong>{shabbat.reading}</strong>
          <span>{shabbat.hebrewDate} · <bdi>{gregorianDate(shabbat.date)}</bdi></span>
        </div>
        {!removal && (
          <div className="name-field">
            <label htmlFor="family-name">שם המשפחה</label>
            <input id="family-name" ref={inputRef} name="familyName" value={name} onChange={event => setName(event.target.value)}
              placeholder="לדוגמה: משפחת כהן…" autoComplete="off" maxLength={100}
              required disabled={pending || conflict}
              aria-invalid={error ? true : undefined} aria-describedby={error ? 'dialog-error' : undefined} />
          </div>
        )}
        {error && <p id="dialog-error" className="inline-error" role="alert">{error}</p>}
        {conflict && <p className="dialog-hint">הלוח מתרענן. סגרו את החלונית ובדקו את השיבוץ העדכני.</p>}
        <div className="dialog-actions">
          <button type="submit" className={removal ? 'button danger' : 'button primary'} disabled={pending || conflict}>
            {pending ? 'שמירה…' : removal ? 'כן, הסרת השיבוץ' : 'שמירת השיבוץ'}
          </button>
          <button type="button" className="button secondary" onClick={onClose} disabled={pending}>ביטול</button>
        </div>
      </form>
    </dialog>
  )
}
