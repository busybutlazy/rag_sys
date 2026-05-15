import { useCallback, useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { apiGet, apiPost } from '../lib/api'

interface ChatMessage {
  id?: string
  role: 'user' | 'assistant'
  content: string
  sources?: { source_id: string; chunk_index: number }[]
  traces?: ToolTrace[]
}

interface ToolTrace {
  step: number
  tool: string
  arguments?: Record<string, unknown>
  ok?: boolean
  summary?: string
}

interface ChatSession {
  id: string
  title: string | null
  messageCount: number
  agentMode: boolean
  createdAt: string
}

interface Props {
  notebookId: string
  getToken: () => string | null
}

export default function ChatPanel({ notebookId, getToken }: Props) {
  const [sessions, setSessions] = useState<ChatSession[]>([])
  const [messagesBySession, setMessagesBySession] = useState<Record<string, ChatMessage[]>>({})
  const [activeSessionId, setActiveSessionId] = useState<string | null>(null)
  const [input, setInput] = useState('')
  const [streaming, setStreaming] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [sessionsOpen, setSessionsOpen] = useState(true)
  const abortRef = useRef<AbortController | null>(null)
  const bottomRef = useRef<HTMLDivElement>(null)

  const activeSession = sessions.find(session => session.id === activeSessionId) ?? sessions[0]
  const activeMessages = activeSession ? (messagesBySession[activeSession.id] ?? []) : []

  function updateSession(sessionId: string, updater: (session: ChatSession) => ChatSession) {
    setSessions(prev => prev.map(session => (session.id === sessionId ? updater(session) : session)))
  }

  function updateMessages(sessionId: string, updater: (messages: ChatMessage[]) => ChatMessage[]) {
    setMessagesBySession(prev => ({ ...prev, [sessionId]: updater(prev[sessionId] ?? []) }))
  }

  const loadSessions = useCallback(async () => {
    setLoading(true)
    try {
      const loaded = await apiGet<ApiSession[]>(`/api/notebooks/${notebookId}/chat-sessions`)
      let next = loaded.map(mapSession)
      if (next.length === 0) {
        const created = await apiPost<ApiSession>(`/api/notebooks/${notebookId}/chat-sessions`, { title: 'New chat', mode: 'chat' })
        next = [mapSession(created)]
      }
      setSessions(next)
      setActiveSessionId(current => current ?? next[0]?.id ?? null)
      setError(null)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load chat sessions')
    } finally {
      setLoading(false)
    }
  }, [notebookId])

  const loadMessages = useCallback(async (sessionId: string) => {
    try {
      const loaded = await apiGet<ApiMessage[]>(`/api/notebooks/${notebookId}/chat-sessions/${sessionId}/messages`)
      setMessagesBySession(prev => ({ ...prev, [sessionId]: loaded.map(mapMessage) }))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load messages')
    }
  }, [notebookId])

  async function addSession() {
    if (streaming) return
    const created = await apiPost<ApiSession>(`/api/notebooks/${notebookId}/chat-sessions`, { title: 'New chat', mode: 'chat' })
    const session = mapSession(created)
    setSessions(prev => [session, ...prev])
    setActiveSessionId(session.id)
    setInput('')
    setError(null)
  }

  function sessionTitle(text: string) {
    return text.length > 32 ? `${text.slice(0, 29)}...` : text
  }

  async function send(e: FormEvent) {
    e.preventDefault()
    const text = input.trim()
    const session = activeSession
    if (!text || streaming || !session) return

    const userMsg: ChatMessage = { role: 'user', content: text }
    const sessionId = session.id
    const assistantIdx = activeMessages.length + 1

    updateSession(sessionId, current => ({
      ...current,
      title: activeMessages.length === 0 ? sessionTitle(text) : current.title,
      messageCount: current.messageCount + 2,
    }))
    updateMessages(sessionId, current => [...current, userMsg, { role: 'assistant', content: '' }])
    setInput('')
    setError(null)
    setStreaming(true)

    const ctrl = new AbortController()
    abortRef.current = ctrl

    try {
      const token = getToken()
      const res = await fetch(`/api/notebooks/${notebookId}/chat-sessions/${sessionId}/runs`, {
        method: 'POST',
        signal: ctrl.signal,
        credentials: 'include',
        headers: {
          'Content-Type': 'application/json',
          ...(token ? { Authorization: `Bearer ${token}` } : {}),
        },
        body: JSON.stringify({
          content: text,
          mode: session.agentMode ? 'agent' : 'chat',
        }),
      })

      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      if (!res.body) throw new Error('No response body')

      const reader = res.body.getReader()
      const decoder = new TextDecoder()
      let buf = ''
      let doneSeen = false

      while (!doneSeen) {
        const { done, value } = await reader.read()
        if (done) break
        buf += decoder.decode(value, { stream: true })

        const lines = buf.split('\n')
        buf = lines.pop() ?? ''

        for (const line of lines) {
          if (!line.startsWith('data: ')) continue
          const raw = line.slice(6)
          if (raw === '[DONE]') {
            doneSeen = true
            break
          }

          try {
            const parsed = JSON.parse(raw)
            if (parsed.error) {
              setError(parsed.error)
              doneSeen = true
              break
            }
            if (parsed.sources) {
              updateMessages(sessionId, current => {
                const next = [...current]
                next[assistantIdx] = { ...next[assistantIdx], sources: parsed.sources }
                return next
              })
            }
            if (parsed.trace) {
              updateMessages(sessionId, current => {
                const next = [...current]
                const traces = next[assistantIdx].traces ?? []
                next[assistantIdx] = {
                  ...next[assistantIdx],
                  traces: [...traces, parsed.trace],
                }
                return next
              })
            }
            if (parsed.tool_result) {
              updateMessages(sessionId, current => {
                const next = [...current]
                const traces = [...(next[assistantIdx].traces ?? [])]
                const idx = traces.findIndex(
                  t => t.step === parsed.tool_result.step && t.tool === parsed.tool_result.tool && t.ok === undefined,
                )
                const resultTrace = {
                  step: parsed.tool_result.step,
                  tool: parsed.tool_result.tool,
                  ok: parsed.tool_result.ok,
                  summary: parsed.tool_result.summary,
                }
                if (idx >= 0) traces[idx] = { ...traces[idx], ...resultTrace }
                else traces.push(resultTrace)
                next[assistantIdx] = { ...next[assistantIdx], traces }
                return next
              })
            }
            if (parsed.token) {
              updateMessages(sessionId, current => {
                const next = [...current]
                next[assistantIdx] = {
                  ...next[assistantIdx],
                  content: next[assistantIdx].content + parsed.token,
                }
                return next
              })
              bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
            }
          } catch {
            // Ignore malformed SSE lines.
          }
        }
      }
    } catch (err: unknown) {
      if (err instanceof Error && err.name !== 'AbortError') {
        setError(err.message)
        updateMessages(sessionId, current => current.filter((_, i) => i !== assistantIdx))
      }
    } finally {
      setStreaming(false)
      abortRef.current = null
      void loadMessages(sessionId)
      void loadSessions()
    }
  }

  function stop() {
    abortRef.current?.abort()
  }

  function toggleAgentMode() {
    if (!activeSession || streaming) return
    updateSession(activeSession.id, session => ({ ...session, agentMode: !session.agentMode }))
  }

  useEffect(() => {
    void loadSessions()
  }, [loadSessions])

  useEffect(() => {
    if (activeSessionId && messagesBySession[activeSessionId] === undefined) {
      void loadMessages(activeSessionId)
    }
  }, [activeSessionId, loadMessages, messagesBySession])

  function formatArgs(args?: Record<string, unknown>) {
    if (!args) return ''
    const text = JSON.stringify(args)
    return text.length > 160 ? `${text.slice(0, 157)}...` : text
  }

  const activeSessionTitle = sessions.find(s => s.id === activeSessionId)?.title ?? 'New chat'

  return (
    <div
      className="min-h-[42rem] overflow-hidden rounded-lg bg-paper"
      style={{
        display: 'grid',
        gridTemplateColumns: sessionsOpen ? '14rem minmax(0,1fr)' : '2.5rem minmax(0,1fr)',
        transition: 'grid-template-columns 0.24s ease',
      }}
    >
      <aside
        style={{
          borderRight: '1px solid var(--ink-rule)',
          background: '#F7F9FB',
          overflow: 'hidden',
          display: 'flex',
          flexDirection: 'column',
        }}
      >
        {sessionsOpen ? (
          <>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '0.75rem 0.75rem 0.5rem', flexShrink: 0 }}>
              <div>
                <p className="eyebrow">Chat</p>
                <h2 style={{ fontSize: '0.8rem', fontWeight: 500, color: 'var(--ink)', marginTop: '0.1rem' }}>Sessions</h2>
              </div>
              <div style={{ display: 'flex', gap: '0.35rem', alignItems: 'center' }}>
                <button type="button" onClick={addSession} disabled={streaming} className="ui-button ui-button-secondary" style={{ height: '1.8rem', padding: '0 0.6rem', fontSize: '0.72rem' }}>
                  New
                </button>
                <button
                  type="button"
                  onClick={() => setSessionsOpen(false)}
                  title="Collapse sessions"
                  style={{
                    display: 'flex', alignItems: 'center', justifyContent: 'center',
                    width: '1.6rem', height: '1.6rem', border: 'none',
                    borderRadius: '0.3rem', background: 'transparent',
                    cursor: 'pointer', color: 'var(--ink-soft)', fontSize: '0.75rem',
                    transition: 'color 0.18s',
                  }}
                >‹</button>
              </div>
            </div>
            <div style={{ flex: 1, overflowY: 'auto', padding: '0 0.5rem 0.75rem' }}>
              {sessions.map(session => (
                <button
                  key={session.id}
                  type="button"
                  onClick={() => { setActiveSessionId(session.id); setError(null) }}
                  style={{
                    display: 'block', width: '100%', textAlign: 'left',
                    padding: '0.5rem 0.6rem', borderRadius: '0.4rem',
                    border: 'none', cursor: 'pointer',
                    background: activeSessionId === session.id ? 'var(--paper)' : 'transparent',
                    boxShadow: activeSessionId === session.id ? '0 1px 3px rgba(0,0,0,0.07)' : 'none',
                    transition: 'background 0.15s',
                    marginBottom: '0.2rem',
                  }}
                  onMouseEnter={e => { if (activeSessionId !== session.id) e.currentTarget.style.background = 'var(--tint)' }}
                  onMouseLeave={e => { if (activeSessionId !== session.id) e.currentTarget.style.background = 'transparent' }}
                >
                  <p style={{ fontSize: '0.8rem', fontWeight: 500, color: 'var(--ink)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{session.title ?? 'New chat'}</p>
                  <p style={{ fontSize: '0.68rem', color: 'var(--ink-soft)', marginTop: '0.15rem' }}>{session.messageCount} messages</p>
                </button>
              ))}
              {!loading && sessions.length === 0 && <div className="empty-state">No sessions.</div>}
            </div>
          </>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', paddingTop: '0.75rem', gap: '0.5rem' }}>
            <button
              type="button"
              onClick={() => setSessionsOpen(true)}
              title="Expand sessions"
              style={{
                display: 'flex', alignItems: 'center', justifyContent: 'center',
                width: '1.8rem', height: '1.8rem', border: '1px solid var(--ink-rule)',
                borderRadius: '0.3rem', background: 'transparent',
                cursor: 'pointer', color: 'var(--ink-soft)', fontSize: '0.75rem',
                transition: 'color 0.18s',
              }}
            >›</button>
            <div style={{
              writingMode: 'vertical-rl', textOrientation: 'mixed',
              fontSize: '0.6rem', fontWeight: 500, letterSpacing: '0.12em',
              textTransform: 'uppercase', color: 'var(--ink-soft)',
              transform: 'rotate(180deg)', marginTop: '0.25rem',
              whiteSpace: 'nowrap', overflow: 'hidden', maxHeight: '7rem',
              textOverflow: 'ellipsis',
            }}>
              {activeSessionTitle}
            </div>
          </div>
        )}
      </aside>

      <div className="flex min-w-0 flex-col">
        <div className="flex items-center justify-between gap-3 border-b border-stone-200 px-4 py-3">
          <div className="min-w-0">
            <p className="truncate text-sm font-semibold text-stone-800">{activeSession?.title ?? 'New chat'}</p>
            <p className="text-xs text-stone-400">{activeSession?.agentMode ? 'Agent mode' : 'Chat mode'}</p>
          </div>
          <button
            type="button"
            onClick={toggleAgentMode}
            disabled={streaming}
            className={`relative inline-flex h-7 w-12 shrink-0 items-center rounded-full transition-colors disabled:opacity-50 ${
              activeSession?.agentMode ? 'bg-matcha' : 'bg-stone-300'
            }`}
            aria-label="Toggle agent mode"
            title="Toggle agent mode"
          >
            <span
              className={`inline-block h-5 w-5 rounded-full bg-white shadow-sm transition-transform ${
                activeSession?.agentMode ? 'translate-x-6' : 'translate-x-1'
              }`}
            />
          </button>
        </div>

        <div className="min-h-0 flex-1 overflow-y-auto p-4" style={{ background: '#F1F4F7' }}>
          {activeMessages.length === 0 && (
            <div className="empty-state mt-8">Ask anything about this notebook.</div>
          )}
          <div className="space-y-4">
            {activeMessages.map((message, i) => (
              <div key={i} className={`flex flex-col ${message.role === 'user' ? 'items-end' : 'items-start'}`}>
                <div
                  className={`max-w-[86%] rounded-lg px-3 py-2 text-sm leading-6 shadow-sm ${
                    message.role === 'user'
                      ? 'bg-rail text-white'
                      : 'border border-stone-200 bg-white text-stone-800'
                  } whitespace-pre-wrap break-words`}
                >
                  {message.content || (streaming && message.role === 'assistant' ? <span className="animate-pulse">|</span> : '')}
                </div>
                {message.role === 'assistant' && message.sources && message.sources.length > 0 && (
                  <details className="mt-2 max-w-[86%] text-xs text-stone-500">
                    <summary className="cursor-pointer hover:text-stone-700">
                      {message.sources.length} source{message.sources.length > 1 ? 's' : ''} cited
                    </summary>
                    <ul className="mt-1 space-y-0.5 pl-2">
                      {message.sources.map((source, si) => (
                        <li key={si} className="truncate font-mono">
                          {source.source_id.slice(0, 8)}... chunk {source.chunk_index}
                        </li>
                      ))}
                    </ul>
                  </details>
                )}
                {message.role === 'assistant' && message.traces && message.traces.length > 0 && (
                  <details className="mt-2 max-w-[86%] text-xs text-stone-500">
                    <summary className="cursor-pointer hover:text-stone-700">
                      {message.traces.length} agent step{message.traces.length > 1 ? 's' : ''}
                    </summary>
                    <ul className="mt-1 space-y-1 pl-2">
                      {message.traces.map((trace, ti) => (
                        <li key={ti} className="rounded-md border border-stone-200 bg-white px-2 py-1">
                          <div className="flex items-center justify-between gap-2">
                            <span className="font-mono text-stone-700">
                              {trace.step}. {trace.tool}
                            </span>
                            {trace.ok !== undefined && (
                              <span className={trace.ok ? 'text-matcha' : 'text-red-500'}>
                                {trace.ok ? 'ok' : 'error'}
                              </span>
                            )}
                          </div>
                          {trace.arguments && (
                            <div className="mt-0.5 truncate font-mono text-stone-400">
                              {formatArgs(trace.arguments)}
                            </div>
                          )}
                          {trace.summary && <div className="mt-0.5 truncate">{trace.summary}</div>}
                        </li>
                      ))}
                    </ul>
                  </details>
                )}
              </div>
            ))}
          </div>
          {error && <p className="mt-4 text-center text-xs text-red-500">{error}</p>}
          <div ref={bottomRef} />
        </div>

        <form onSubmit={send} className="flex gap-2 border-t border-stone-200 bg-paper p-3">
          <input
            value={input}
            onChange={e => setInput(e.target.value)}
            disabled={streaming}
            placeholder="Type a message"
            className="ui-input"
          />
          {streaming ? (
            <button type="button" onClick={stop} className="ui-button ui-button-danger">
              Stop
            </button>
          ) : (
            <button type="submit" disabled={!input.trim()} className="ui-button ui-button-primary">
              Send
            </button>
          )}
        </form>
      </div>
    </div>
  )
}

interface ApiSession {
  id: string
  title: string | null
  mode: string
  createdAt: string
  messageCount: number
}

interface ApiMessage {
  id: string
  role: 'user' | 'assistant'
  content: string
  sources?: { source_id: string; chunk_index: number }[] | null
  traces?: ToolTrace[] | null
}

function mapSession(session: ApiSession): ChatSession {
  return {
    id: session.id,
    title: session.title,
    messageCount: session.messageCount ?? 0,
    agentMode: session.mode === 'agent',
    createdAt: session.createdAt,
  }
}

function mapMessage(message: ApiMessage): ChatMessage {
  return {
    id: message.id,
    role: message.role,
    content: message.content,
    sources: message.sources ?? undefined,
    traces: message.traces ?? undefined,
  }
}
