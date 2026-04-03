import { apiGet, apiPost, apiPut, apiDelete } from './client'
import type { Profile, ProfileParameter } from './types'

interface BackendProfile {
  id: number
  name: string
  modelPath: string
  selectedBinaryId: number | null
  parametersJson: string | null
  customArgsJson: string | null
  createdAt: string
  updatedAt: string
}

function fromBackend(b: BackendProfile): Profile {
  return {
    id: b.id,
    name: b.name,
    modelPath: b.modelPath,
    selectedBinaryId: b.selectedBinaryId,
    parameters: b.parametersJson ? JSON.parse(b.parametersJson) : {},
    customArgs: b.customArgsJson ? JSON.parse(b.customArgsJson) : [],
    createdAt: b.createdAt,
    updatedAt: b.updatedAt,
  }
}

function toBackend(p: Partial<Profile>): object {
  return {
    name: p.name,
    modelPath: p.modelPath,
    selectedBinaryId: p.selectedBinaryId ?? null,
    parametersJson: p.parameters !== undefined ? JSON.stringify(p.parameters) : undefined,
    customArgsJson: p.customArgs !== undefined ? JSON.stringify(p.customArgs) : undefined,
  }
}

export const profilesApi = {
  getAll: () => apiGet<BackendProfile[]>('/profiles').then(list => list.map(fromBackend)),
  getById: (id: number) => apiGet<BackendProfile>(`/profiles/${id}`).then(fromBackend),
  create: (data: Partial<Profile>) => apiPost<BackendProfile>('/profiles', toBackend(data)).then(fromBackend),
  update: (id: number, data: Partial<Profile>) => apiPut<BackendProfile>(`/profiles/${id}`, toBackend(data)).then(fromBackend),
  delete: (id: number) => apiDelete(`/profiles/${id}`),
  clone: (id: number) => apiPost<BackendProfile>(`/profiles/${id}/clone`).then(fromBackend),
  launch: (id: number) => apiPost(`/profiles/${id}/launch`),
}

export type { ProfileParameter }
