import { apiGet, apiPost, apiPut, apiDelete } from './client'
import type { LlamaServerBinary } from './types'

export const binariesApi = {
  getAll: () => apiGet<LlamaServerBinary[]>('/binaries'),
  create: (dto: { name: string; path: string; isDefault?: boolean }) =>
    apiPost<LlamaServerBinary>('/binaries', dto),
  update: (id: number, dto: { name?: string; path?: string; isDefault?: boolean }) =>
    apiPut<LlamaServerBinary>(`/binaries/${id}`, dto),
  delete: (id: number) => apiDelete(`/binaries/${id}`),
}
