import { apiGet } from './client'
import type { SystemStatus } from './types'

export const systemApi = {
  getStatus: () => apiGet<SystemStatus>('/system/status'),
  getModels: () => apiGet<string[]>('/system/models'),
  getGpus: () => apiGet<string[]>('/system/gpus'),
}
