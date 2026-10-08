import { Link, Route, Routes } from 'react-router-dom'
import CalendarPage from './CalendarPage'
import { CalendarIcon } from './Icon'
import './styles.css'

export default function App() {
  return (
    <div className="app-shell" dir="rtl">
      <a className="skip-link" href="#main">דילוג לתוכן</a>
      <header className="site-header">
        <div className="header-content">
          <Link to="/" className="brand" aria-label="לוח שבתות — עמוד הבית">
            <span className="brand-icon"><CalendarIcon /></span>
            <span>לוח שבתות<small>נפגשים סביב השולחן</small></span>
          </Link>
          <span className="header-label"><span className="status-dot" /> לוח קהילתי · ארץ ישראל</span>
        </div>
      </header>
      <Routes>
        <Route path="/" element={<CalendarPage />} />
        <Route path="*" element={<main id="main" className="page"><h1>העמוד לא נמצא</h1><Link to="/">חזרה ללוח השבתות</Link></main>} />
      </Routes>
      <footer className="site-footer">
        <span>שבת טובה מתחילה ביחד.</span>
        <span>נתוני לוח השנה באדיבות <a href="https://www.hebcal.com/" target="_blank" rel="noreferrer">Hebcal</a>
          {' '}· סדר הקריאה הישראלי · <a href="https://creativecommons.org/licenses/by/4.0/" target="_blank" rel="noreferrer">CC BY 4.0</a></span>
      </footer>
    </div>
  )
}
