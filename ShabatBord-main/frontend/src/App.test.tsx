import { render, screen, within, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App'

const rows = [
  {
    date: '2027-01-02', hebrewDate: 'כ״ג טבת תשפ״ז', reading: 'פרשת שמות',
    isFestival: false, isMevarchim: true, blessedMonth: 'שבט',
    roshChodeshDates: ['2027-01-09'], assignment: null,
  },
  {
    date: '2027-01-09', hebrewDate: 'א׳ שבט תשפ״ז', reading: 'פרשת וארא',
    isFestival: false, isMevarchim: false, blessedMonth: null,
    roshChodeshDates: [],
    assignment: { date: '2027-01-09', familyName: 'משפחת לוי', version: 'version-1' },
  },
]

function mount() {
  render(<MemoryRouter initialEntries={['/?year=2027']}><App /></MemoryRouter>)
}

describe('calendar', () => {
  const fetchMock = vi.fn<typeof fetch>()

  beforeEach(() => {
    fetchMock.mockReset()
    fetchMock.mockImplementation(async () =>
      new Response(JSON.stringify({ year: 2027, shabbatot: rows }), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)
  })

  it('shows Gregorian-only year selection, all Shabbatot and no families navigation', async () => {
    mount()
    expect(await screen.findByText('פרשת שמות')).toBeVisible()
    expect(screen.getByText('פרשת וארא')).toBeVisible()
    expect(screen.getByLabelText('מחזור המתחיל בשנת')).toHaveValue('2027')
    expect(screen.queryByText('ניהול משפחות')).not.toBeInTheDocument()
    expect(screen.queryByText('משפחות')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /בחרו שבת/ })).toBeVisible()
  })

  it('filters Mevarchim and unassigned Shabbatot', async () => {
    const user = userEvent.setup()
    mount()
    await screen.findByText('פרשת וארא')
    await user.selectOptions(screen.getByLabelText('הצגת שבתות'), 'mevarchim')
    expect(screen.queryByText('פרשת וארא')).not.toBeInTheDocument()
    expect(screen.getByText('פרשת שמות')).toBeVisible()
    await user.selectOptions(screen.getByLabelText('הצגת שבתות'), 'unassigned')
    expect(screen.queryByText('משפחת לוי')).not.toBeInTheDocument()
  })

  it('claims a Shabbat with a directly entered family name', async () => {
    const user = userEvent.setup()
    fetchMock.mockImplementation(async (_input, options) => {
      if (options?.method === 'POST')
        return new Response(JSON.stringify({ date: '2027-01-02', familyName: 'משפחת כהן', version: 'new' }), { status: 201 })
      return new Response(JSON.stringify({ year: 2027, shabbatot: rows }), { status: 200 })
    })
    mount()
    await user.click(await screen.findByRole('button', { name: /בחרו שבת/ }))
    const dialog = screen.getByRole('dialog')
    await user.type(within(dialog).getByLabelText('שם המשפחה'), 'משפחת כהן')
    await user.click(within(dialog).getByRole('button', { name: 'שמירת השיבוץ' }))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/assignments/2027-01-02',
      expect.objectContaining({ method: 'POST', body: JSON.stringify({ familyName: 'משפחת כהן' }) })))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(screen.getByRole('status')).toHaveTextContent('השיבוץ נשמר')
  })

  it('edits an assignment with its version and confirms removal', async () => {
    const user = userEvent.setup()
    fetchMock.mockImplementation(async (_input, options) => {
      if (options?.method === 'DELETE') return new Response(null, { status: 204 })
      if (options?.method === 'PUT')
        return new Response(JSON.stringify({ date: '2027-01-09', familyName: 'משפחת ישראלי', version: 'new' }), { status: 200 })
      return new Response(JSON.stringify({ year: 2027, shabbatot: rows }), { status: 200 })
    })
    mount()
    await user.click(await screen.findByRole('button', { name: /עריכת השיבוץ/ }))
    let dialog = screen.getByRole('dialog')
    await user.clear(within(dialog).getByLabelText('שם המשפחה'))
    await user.type(within(dialog).getByLabelText('שם המשפחה'), 'משפחת ישראלי')
    await user.click(within(dialog).getByRole('button', { name: 'שמירת השיבוץ' }))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/assignments/2027-01-09',
      expect.objectContaining({ method: 'PUT', body: JSON.stringify({ familyName: 'משפחת ישראלי', version: 'version-1' }) })))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    await user.click(screen.getByRole('button', { name: /הסרת השיבוץ/ }))
    dialog = screen.getByRole('dialog')
    expect(within(dialog).getByText(/האם להסיר/)).toBeVisible()
    await user.click(within(dialog).getByRole('button', { name: 'כן, הסרת השיבוץ' }))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/assignments/2027-01-09?version=version-1',
      expect.objectContaining({ method: 'DELETE' })))
  })

  it('shows explicit upstream error and supports retry', async () => {
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify({ detail: 'שירות הלוח אינו זמין' }), { status: 502 }))
    const user = userEvent.setup()
    mount()
    expect(await screen.findByRole('alert')).toHaveTextContent('שירות הלוח אינו זמין')
    await user.click(screen.getByRole('button', { name: 'ניסיון נוסף' }))
    expect(await screen.findByText('פרשת שמות')).toBeVisible()
  })

  it('keeps the dialog open and refreshes after a claim conflict', async () => {
    fetchMock.mockImplementation(async (_input, options) => options?.method === 'POST'
      ? new Response(JSON.stringify({ detail: 'השבת כבר נבחרה' }), { status: 409 })
      : new Response(JSON.stringify({ year: 2027, shabbatot: rows }), { status: 200 }))
    const user = userEvent.setup()
    mount()
    await user.click(await screen.findByRole('button', { name: /בחרו שבת/ }))
    const dialog = screen.getByRole('dialog')
    await user.type(within(dialog).getByLabelText('שם המשפחה'), 'Cohen')
    await user.click(within(dialog).getByRole('button', { name: 'שמירת השיבוץ' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('השבת כבר נבחרה')
    expect(fetchMock.mock.calls.filter(([, options]) => !options?.method).length).toBeGreaterThanOrEqual(2)
  })

  it('navigates Gregorian years without introducing Hebrew-year selection', async () => {
    const user = userEvent.setup()
    mount()
    await screen.findByText('פרשת שמות')
    await user.selectOptions(screen.getByLabelText('מחזור המתחיל בשנת'), '2028')
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/calendar?year=2028',
      expect.objectContaining({ signal: expect.any(AbortSignal) })))
  })

  it('starts with Bereshit and describes the chronological range into the next year', async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify({
      year: 2027,
      shabbatot: [
        { ...rows[0], date: '2027-10-30', hebrewDate: 'כ״ט תשרי תשפ״ח', reading: 'פרשת בראשית' },
        { ...rows[1], date: '2028-10-07', hebrewDate: 'י״ז תשרי תשפ״ט', reading: 'סוכות ג׳ (חוה״מ)', assignment: null },
      ],
    }), { status: 200 }))
    mount()
    expect(await screen.findByText('פרשת בראשית')).toBeVisible()
    expect(screen.getByLabelText('טווח תאריכי המחזור')).toHaveTextContent('30.10.2027')
    expect(screen.getByLabelText('טווח תאריכי המחזור')).toHaveTextContent('07.10.2028')
    expect(within(screen.getAllByRole('row')[1]).getByText('פרשת בראשית')).toBeVisible()
    expect(screen.getByText('שבתות במחזור')).toBeVisible()
  })
})
