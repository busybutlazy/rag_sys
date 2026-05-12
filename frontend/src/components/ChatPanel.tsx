import { useRef, useState } from 'react'

interface ChatMessage {
  role: 'user' | 'assistant'
  content: string
  sources?: { source_id: string; chunk_index: number }[]
}

interface Props {
  notebookId: string
  getToken: () => string | null
}

export default function ChatPanel({ notebookId, getToken }: Props) {
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [input, setInput] = useState('')
  const [streaming, setStreaming] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const abortRef = useRef<AbortController | null>(null)
  const bottomRef = useRef<HTMLDivElement>(null)

  async function send(e: React.FormEvent) {
    e.preventDefault()
    const text = input.trim()
    if (!text || streaming) return

    const userMsg: ChatMessage = { role: 'user', content: text }
    const history = [...messages, userMsg]
    setMessages(history)
    setInput('')
    setError(null)
    setStreaming(true)

    const assistantIdx = history.length
    setMessages(prev => [...prev, { role: 'assistant', content: '' }])

    const ctrl = new AbortController()
    abortRef.current = ctrl

    try {
      const token = getToken()
      const res = await fetch('/ai/chat/completions', {
        method: 'POST',
        signal: ctrl.signal,
        credentials: 'include',
        headers: {
          'Content-Type': 'application/json',
          ...(token ? { Authorization: `Bearer ${token}` } : {}),
        },
        body: JSON.stringify({
          messages: history.map(m => ({ role: m.role, content: m.content })),
          notebook_id: notebookId,
        }),
      })

      if (!res.ok) throw new Error(`HTTP ${res.status}`)

      const reader = res.body!.getReader()
      const decoder = new TextDecoder()
      let buf = ''

      while (true) {
        const { done, value } = await reader.read()
        if (done) break
        buf += decoder.decode(value, { stream: true })

        const lines = buf.split('\n')
        buf = lines.pop() ?? ''

        for (const line of lines) {
          if (!line.startsWith('data: ')) continue
          const raw = line.slice(6)
          if (raw === '[DONE]') break
          try {
            const parsed = JSON.parse(raw)
            if (parsed.error) {
              setError(parsed.error)
              break
            }
            if (parsed.sources) {
              setMessages(prev => {
                const next = [...prev]
                next[assistantIdx] = { ...next[assistantIdx], sources: parsed.sources }
                return next
              })
            }
            if (parsed.token) {
              setMessages(prev => {
                const next = [...prev]
                next[assistantIdx] = {
                  ...next[assistantIdx],
                  content: next[assistantIdx].content + parsed.token,
                }
                return next
              })
              bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
            }
          } catch {
            // ignore malformed SSE line
          }
        }
      }
    } catch (err: unknown) {
      if (err instanceof Error && err.name !== 'AbortError') {
        setError(err.message)
        setMessages(prev => prev.filter((_, i) => i !== assistantIdx))
      }
    } finally {
      setStreaming(false)
      abortRef.current = null
    }
  }

  function stop() {
    abortRef.current?.abort()
  }

  return (
    <div className="flex flex-col h-[28rem] border rounded-lg overflow-hidden">
      <div className="flex-1 overflow-y-auto p-4 space-y-3 bg-gray-50">
        {messages.length === 0 && (
          <p className="text-sm text-gray-400 text-center mt-8">Ask anything about this notebook…</p>
        )}
        {messages.map((m, i) => (
          <div key={i} className={`flex flex-col ${m.role === 'user' ? 'items-end' : 'items-start'}`}>
            <div
              className={`max-w-[80%] rounded-lg px-3 py-2 text-sm whitespace-pre-wrap break-words ${
                m.role === 'user'
                  ? 'bg-blue-600 text-white'
                  : 'bg-white border text-gray-800'
              }`}
            >
              {m.content || (streaming && m.role === 'assistant' ? <span className="animate-pulse">▋</span> : '')}
            </div>
            {m.role === 'assistant' && m.sources && m.sources.length > 0 && (
              <details className="mt-1 text-xs text-gray-500 max-w-[80%]">
                <summary className="cursor-pointer hover:text-gray-700">
                  {m.sources.length} source{m.sources.length > 1 ? 's' : ''} cited
                </summary>
                <ul className="mt-1 space-y-0.5 pl-2">
                  {m.sources.map((s, si) => (
                    <li key={si} className="font-mono truncate">
                      {s.source_id.slice(0, 8)}… chunk {s.chunk_index}
                    </li>
                  ))}
                </ul>
              </details>
            )}
          </div>
        ))}
        {error && <p className="text-xs text-red-500 text-center">{error}</p>}
        <div ref={bottomRef} />
      </div>

      <form onSubmit={send} className="flex gap-2 p-3 border-t bg-white">
        <input
          value={input}
          onChange={e => setInput(e.target.value)}
          disabled={streaming}
          placeholder="Type a message…"
          className="flex-1 border rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:opacity-50"
        />
        {streaming ? (
          <button
            type="button"
            onClick={stop}
            className="px-3 py-2 bg-red-500 text-white rounded-lg text-sm hover:bg-red-600"
          >
            Stop
          </button>
        ) : (
          <button
            type="submit"
            disabled={!input.trim()}
            className="px-3 py-2 bg-blue-600 text-white rounded-lg text-sm hover:bg-blue-700 disabled:opacity-40"
          >
            Send
          </button>
        )}
      </form>
    </div>
  )
}
