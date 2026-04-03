import { apiGet, apiPost, apiClient } from './client'
import type { BenchmarkResult } from './types'

export type BenchmarkStreamEvent =
  | { type: 'phase'; phase: string }
  | { type: 'token'; n: number; tps?: number; timeS?: number; promptTps?: number; promptMs?: number }
  | { type: 'log'; log: string }
  | { type: 'done'; result: BenchmarkResult; promptTps?: number; promptMs?: number }
  | { type: 'error'; error: string }
  | { type: 'prompt_progress'; promptProgress: number }
  | { type: 'round'; round: number; totalRounds: number; inputTokens: number; outputTokens: number; roundTtftMs: number; roundSpeedTps: number }

export interface BenchmarkProgressPoint { timeS: number; tps: number; tokens: number }
export interface BenchmarkProgress {
  isRunning: boolean
  phase: string
  tokenCount: number
  nPredict: number
  genTps: number
  promptTps: number
  promptMs: number
  error: string | null
  resultId: number | null
  points: BenchmarkProgressPoint[]
}

export const benchmarksApi = {
  getAll: () => apiGet<BenchmarkResult[]>('/benchmarks'),
  getById: (id: number) => apiGet<BenchmarkResult>(`/benchmarks/${id}`),
  run: (profileId: number, notes?: string, nPredict?: number) => apiPost<BenchmarkResult>('/benchmarks/run', { profileId, notes, nPredict }),
  compare: (ids: number[]) => apiGet<BenchmarkResult[]>(`/benchmarks/compare?ids=${ids.join(',')}`),
  delete: (ids: number[]) => apiClient.delete(`/benchmarks?ids=${ids.join(',')}`),

  start: (profileId: number, notes: string | undefined, nPredict: number) =>
    apiPost<{ started: boolean }>('/benchmarks/start', { profileId, notes, nPredict }),
  getProgress: () => apiGet<BenchmarkProgress | null>('/benchmarks/progress'),
  cancel: () => apiPost<{ cancelled: boolean }>('/benchmarks/cancel', {}),

  runStream: async (
    profileId: number,
    notes: string | undefined,
    nPredict: number,
    onEvent: (e: BenchmarkStreamEvent) => void,
    signal?: AbortSignal,
    benchmarkType: string = 'token-generation',
    agentRounds?: number,
  ): Promise<BenchmarkResult> => {
    const response = await fetch('/api/benchmarks/run-stream', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ profileId, notes, nPredict, benchmarkType, agentRounds }),
      signal,
    })
    if (!response.ok) throw new Error(`Server error: ${response.status}`)
    const reader = response.body!.getReader()
    const decoder = new TextDecoder()
    let buffer = ''

    while (true) {
      const { done, value } = await reader.read()
      if (done) break
      buffer += decoder.decode(value, { stream: true })
      const chunks = buffer.split('\n\n')
      buffer = chunks.pop() ?? ''
      for (const chunk of chunks) {
        const line = chunk.trim()
        if (!line.startsWith('data: ')) continue
        const evt: BenchmarkStreamEvent = JSON.parse(line.slice(6))
        onEvent(evt)
        if (evt.type === 'done') return evt.result
        if (evt.type === 'error') throw new Error(evt.error)
      }
    }
    throw new Error('Stream ended without a result')
  },

  exportCsv: async () => {
    const res = await apiClient.get('/benchmarks/export', { responseType: 'blob' })
    const url = URL.createObjectURL(res.data)
    const a = document.createElement('a')
    a.href = url
    a.download = 'benchmarks.csv'
    a.click()
    URL.revokeObjectURL(url)
  },
}
