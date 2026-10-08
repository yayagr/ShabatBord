export function CalendarIcon({ className = '' }: { className?: string }) {
  return (
    <svg className={className} width="24" height="24" viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <rect x="3" y="5" width="18" height="16" rx="3" stroke="currentColor" strokeWidth="1.6" />
      <path d="M7 3v4m10-4v4M3 10h18" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" />
      <path d="m8 15 2.5 2.5L16 12" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )
}

export function Chevron({ direction }: { direction: 'right' | 'left' }) {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <path d={direction === 'right' ? 'm9 6 6 6-6 6' : 'm15 6-6 6 6 6'} stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )
}
