let _getToken: (() => string | null) | null = null

export function registerTokenGetter(fn: () => string | null) {
  _getToken = fn
}

async function apiFetch(path: string, init?: RequestInit): Promise<Response> {
  const token = _getToken?.()
  return fetch(path, {
    ...init,
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...init?.headers,
    },
  })
}

export async function apiGet<T>(path: string): Promise<T> {
  const res = await apiFetch(path)
  if (!res.ok) throw new Error(`GET ${path} → ${res.status}`)
  return res.json()
}

export async function apiPost<T>(path: string, body: unknown): Promise<T> {
  const res = await apiFetch(path, { method: 'POST', body: JSON.stringify(body) })
  if (!res.ok) throw new Error(`POST ${path} → ${res.status}`)
  return res.json()
}

export async function apiPut<T>(path: string, body: unknown): Promise<T> {
  const res = await apiFetch(path, { method: 'PUT', body: JSON.stringify(body) })
  if (!res.ok) throw new Error(`PUT ${path} → ${res.status}`)
  return res.json()
}

export async function apiDelete(path: string): Promise<void> {
  const res = await apiFetch(path, { method: 'DELETE' })
  if (!res.ok) throw new Error(`DELETE ${path} → ${res.status}`)
}

export async function apiUpload<T>(path: string, file: File): Promise<T> {
  const token = _getToken?.()
  const form = new FormData()
  form.append('file', file)
  const res = await fetch(path, {
    method: 'POST',
    credentials: 'include',
    headers: token ? { Authorization: `Bearer ${token}` } : {},
    body: form,
  })
  if (!res.ok) throw new Error(`UPLOAD ${path} → ${res.status}`)
  return res.json()
}
