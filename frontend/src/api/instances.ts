import { apiGet, apiPost, apiPut, apiDelete } from './client'
import type { Instance, Metrics } from './types'

export const instancesApi = {
  getAll: () => apiGet<Instance[]>('/instances'),
  getById: (id: number) => apiGet<Instance>(`/instances/${id}`),
  create: (data: { name: string; profileId: number; port: number }) => apiPost<Instance>('/instances', data),
  update: (id: number, data: Partial<{ name: string; port: number }>) => apiPut<Instance>(`/instances/${id}`, data),
  delete: (id: number) => apiDelete(`/instances/${id}`),
  start: (id: number) => apiPost(`/instances/${id}/start`),
  stop: (id: number) => apiPost(`/instances/${id}/stop`),
  getMetrics: (id: number) => apiGet<Metrics>(`/instances/${id}/metrics`),
  getLogs: (instanceId: number) => apiGet<{ lines: string[] }>(`/logs/${instanceId}`),
}
