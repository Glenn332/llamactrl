import { apiClient, apiGet, apiPost, apiPut } from './client'
import type { AppSettings } from './types'

export const settingsApi = {
  get: () => apiGet<AppSettings>('/settings'),
  update: (data: Partial<AppSettings>) => apiPut<AppSettings>('/settings', data),

  exportDb: async () => {
    const res = await apiClient.get('/settings/export-db', { responseType: 'blob' })
    const url = URL.createObjectURL(res.data)
    const a = document.createElement('a')
    a.href = url
    a.download = 'llamactrl.db'
    a.click()
    URL.revokeObjectURL(url)
  },

  importDb: async (file: File) => {
    const formData = new FormData()
    formData.append('file', file)
    const res = await apiClient.post<{ success: boolean; data: string; error?: string }>(
      '/settings/import-db',
      formData,
    )
    if (!res.data.success) throw new Error(res.data.error ?? 'Import failed')
    return res.data.data
  },

  resetToDefaults: () => apiPost<AppSettings>('/settings/reset'),

  openDataDir: () => apiPost<string>('/settings/open-data-dir'),
}
