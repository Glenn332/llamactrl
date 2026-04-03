import { apiGet, apiPost, apiDelete } from './client'
import type { LocalModel, HfSearchResult, HfModelInfo, DownloadProgress } from './types'

export const modelsApi = {
  getActiveDownloads: () => apiGet<DownloadProgress[]>('/models/downloads/active'),
  getLocal: () => apiGet<LocalModel[]>('/models/local'),
  search: (q: string, limit = 20) => apiGet<HfSearchResult[]>(`/models/search?q=${encodeURIComponent(q)}&limit=${limit}`),
  getHfModel: (modelId: string) => apiGet<HfModelInfo>(`/models/hf?modelId=${encodeURIComponent(modelId)}`),
  checkHfCli: () => apiGet<{ available: boolean }>('/models/hf-cli-available'),
  startDownload: (modelId: string, filename: string, knownSizeBytes?: number, extra?: { author?: string | null; description?: string | null; tags?: string[] | null; hfDownloads?: number | null; hfLikes?: number | null }, targetDirectory?: string) => apiPost<{ downloadId: string }>('/models/download', { modelId, filename, knownSizeBytes, ...extra, targetDirectory }),
  cancelDownload: (downloadId: string) => apiDelete(`/models/download?downloadId=${encodeURIComponent(downloadId)}`),
  getDownloadProgress: (downloadId: string) => apiGet<DownloadProgress | null>(`/models/download/progress/${encodeURIComponent(downloadId)}`),
}
