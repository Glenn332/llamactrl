import { apiGet, apiPost, apiPut, apiDelete } from './client'
import type { ModelDirectory } from './types'

export const modelDirectoriesApi = {
  getAll: () => apiGet<ModelDirectory[]>('/model-directories'),
  create: (dto: { name: string; path: string }) =>
    apiPost<ModelDirectory>('/model-directories', dto),
  update: (id: number, dto: { name?: string; path?: string }) =>
    apiPut<ModelDirectory>(`/model-directories/${id}`, dto),
  delete: (id: number) => apiDelete(`/model-directories/${id}`),
}
