import { useNavigate } from 'react-router-dom'
import { useAuth } from '../hooks/useAuth'

export default function DashboardPage() {
  const { username } = useAuth()
  const navigate = useNavigate()

  return (
    <div style={{ maxWidth: '52rem', margin: '0 auto' }}>

      {/* ── Hero — Re Loop .kv_text style with large display text ── */}
      <section style={{ marginBottom: '6rem' }}>
        <p className="eyebrow" style={{ marginBottom: '1.6rem' }}>Knowledge Desk</p>
        <h1 className="page-title" style={{ marginBottom: '2.4rem' }}>
          Your Knowledge,<br />
          <span style={{ color: 'var(--ink-soft)' }}>Structured.</span>
        </h1>
        <div style={{ width: '3rem', height: '1px', background: 'var(--ink-rule)', margin: '2.4rem 0' }} />
        <p style={{ fontSize: '0.95rem', lineHeight: '1.8', color: 'var(--ink-soft)', maxWidth: '36rem' }}>
          Signed in as{' '}
          <span style={{ color: 'var(--ink)', fontWeight: 500 }}>{username}</span>.
          {' '}Manage your sources, write notes, and query your knowledge base from the Notebooks section.
        </p>
      </section>

      {/* ── Quick actions — Re Loop projects list style ── */}
      <section>
        <div style={{ display: 'flex', alignItems: 'baseline', gap: '1.6rem', marginBottom: '1.6rem' }}>
          <span className="section-num en exl">01</span>
          <p className="eyebrow">Get Started</p>
        </div>

        {/* Re Loop row divider before list */}
        <div style={{ borderTop: '1px solid var(--ink-rule)', borderBottom: '1px solid var(--ink-rule)', padding: '0' }}>
          <button
            onClick={() => navigate('/notebooks')}
            style={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              width: '100%',
              padding: '2rem 0',
              background: 'transparent',
              border: 'none',
              borderBottom: '1px solid var(--ink-rule)',
              cursor: 'pointer',
              textAlign: 'left',
              transition: 'opacity 0.18s',
            }}
            onMouseEnter={e => (e.currentTarget.style.opacity = '0.6')}
            onMouseLeave={e => (e.currentTarget.style.opacity = '1')}
          >
            <div>
              <p style={{ fontSize: '0.72rem', fontWeight: 500, textTransform: 'uppercase', letterSpacing: '0.14em', color: 'var(--ink-soft)', marginBottom: '0.5rem' }}>Action</p>
              <p style={{ fontSize: '1.1rem', fontWeight: 400, color: 'var(--ink)' }}>Open Notebooks →</p>
            </div>
            <p style={{ fontSize: '0.85rem', color: 'var(--ink-soft)', fontFamily: '"Albert Sans", sans-serif', fontWeight: 200 }}>Library</p>
          </button>
        </div>
      </section>

    </div>
  )
}
