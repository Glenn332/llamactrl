import axios from 'axios'

export const apiClient = axios.create({ baseURL: '/api' })

apiClient.interceptors.response.use(
  res => res,
  err => {
    const message = err.response?.data?.error
    return Promise.reject(message ? new Error(message) : err)
  }
)

export async function apiGet<T>(path: string): Promise<T> {
  const res = await apiClient.get<{ success: boolean; data: T; error?: string }>(path)
  if (!res.data.success) throw new Error(res.data.error ?? 'Request failed')
  return res.data.data!
}

export async function apiPost<T>(path: string, body?: unknown): Promise<T> {
  const res = await apiClient.post<{ success: boolean; data: T; error?: string }>(path, body)
  if (!res.data.success) throw new Error(res.data.error ?? 'Request failed')
  return res.data.data!
}

export async function apiPut<T>(path: string, body: unknown): Promise<T> {
  const res = await apiClient.put<{ success: boolean; data: T; error?: string }>(path, body)
  if (!res.data.success) throw new Error(res.data.error ?? 'Request failed')
  return res.data.data!
}

export async function apiDelete(path: string): Promise<void> {
  await apiClient.delete(path)
}
