import { useState, useRef, useCallback } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import {
  LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip,
  Legend, ResponsiveContainer, ReferenceLine, BarChart, Bar,
} from 'recharts'
import { benchmarksApi } from '../api/benchmarks'
import { profilesApi } from '../api/profiles'
import type { BenchmarkResult } from '../api/types'
import { Loader2, Play, Download, BarChart3, X, CheckCircle2, Cpu, Trash2, Zap, Bot, ArrowLeft } from 'lucide-react'
import type { AgentRoundResult } from '../api/types'

type Phase = 'idle' | 'starting' | 'loading' | 'generating' | 'done' | 'error'

interface LivePoint { timeS: number; tps: number; tokens: number }

interface LiveRound {
  round: number
  totalRounds: number
  inputTokens: number
  outputTokens: number
  ttftMs: number
  speedTps: number
}

interface RunState {
  phase: Phase
  elapsed: number
  tokenCount: number
  nPredict: number
  genPoints: LivePoint[]
  genTps: number
  promptTps: number
  promptMs: number
  promptProgress: number
  error: string | null
  liveRounds: LiveRound[]
}

const INIT_RUN: RunState = {
  phase: 'idle', elapsed: 0, tokenCount: 0, nPredict: 2000,
  genPoints: [], genTps: 0, promptTps: 0, promptMs: 0, promptProgress: 0, error: null,
  liveRounds: [],
}

function fmtElapsed(ms: number) {
  const s = Math.floor(ms / 1000)
  const m = Math.floor(s / 60)
  return m > 0 ? `${m}m ${s % 60}s` : `${s}s`
}

const PHASE_LABELS: Record<string, string> = {
  starting:   'Starting llama-server',
  loading:    'Loading model into memory',
  generating: 'Running completion benchmark',
}

export function Benchmarks() {
  const queryClient = useQueryClient()
  const [selected, setSelected] = useState<Set<number>>(new Set())
  const [showRunModal, setShowRunModal] = useState(false)
  const [runProfileId, setRunProfileId] = useState<number>(0)
  const [runNotes, setRunNotes] = useState('')
  const [nPredict, setNPredict] = useState(2000)
  const [benchmarkType, setBenchmarkType] = useState<'token-generation' | 'agentic'>('token-generation')
  const [modalStep, setModalStep] = useState<1 | 2>(1)
  const [agentRounds, setAgentRounds] = useState(4)
  const [agentOutputTokens, setAgentOutputTokens] = useState(512)
  const [agentInputTokens, setAgentInputTokens] = useState(512)
  const [comparing, setComparing] = useState<BenchmarkResult[] | null>(null)
  const [viewingResult, setViewingResult] = useState<BenchmarkResult | null>(null)

  const [run, setRun] = useState<RunState>(INIT_RUN)
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null)
  const abortRef = useRef<AbortController | null>(null)
  const startTimeRef = useRef<number>(0)

  const { data: benchmarks, isLoading, error } = useQuery({
    queryKey: ['benchmarks'],
    queryFn: benchmarksApi.getAll,
  })

  const { data: profiles } = useQuery({
    queryKey: ['profiles'],
    queryFn: profilesApi.getAll,
  })

  const profileName = profiles?.find(p => p.id === runProfileId)?.name ?? ''

  const stopTimer = () => {
    if (timerRef.current) { clearInterval(timerRef.current); timerRef.current = null }
  }

  const startRun = useCallback(async () => {
    if (!runProfileId) return
    setShowRunModal(false)
    setModalStep(1)
    startTimeRef.current = Date.now()
    setRun({ ...INIT_RUN, phase: 'starting', nPredict })

    timerRef.current = setInterval(() => {
      setRun(prev => ({ ...prev, elapsed: Date.now() - startTimeRef.current }))
    }, 200)

    const abort = new AbortController()
    abortRef.current = abort

    try {
      await benchmarksApi.runStream(
        runProfileId,
        runNotes || undefined,
        nPredict,
        (evt) => {
          if (evt.type === 'phase') {
            setRun(prev => ({ ...prev, phase: evt.phase as Phase }))
          } else if (evt.type === 'prompt_progress') {
            setRun(r => r ? { ...r, promptProgress: evt.promptProgress } : r)
          } else if (evt.type === 'token') {
            setRun(prev => {
              const pts = evt.tps != null && evt.timeS != null
                ? [...prev.genPoints, { timeS: evt.timeS, tps: evt.tps, tokens: evt.n }]
                : prev.genPoints
              return {
                ...prev,
                tokenCount: evt.n,
                genTps: evt.tps ?? prev.genTps,
                genPoints: pts,
                promptTps: evt.promptTps != null ? evt.promptTps : prev.promptTps,
                promptMs:  evt.promptMs  != null ? evt.promptMs  : prev.promptMs,
                promptProgress: 100, // first token arrived — prompt processing complete
              }
            })
          } else if (evt.type === 'round') {
            setRun(prev => ({
              ...prev,
              liveRounds: [...prev.liveRounds, {
                round: evt.round,
                totalRounds: evt.totalRounds,
                inputTokens: evt.inputTokens,
                outputTokens: evt.outputTokens,
                ttftMs: evt.roundTtftMs,
                speedTps: evt.roundSpeedTps,
              }],
            }))
          } else if (evt.type === 'done') {
            stopTimer()
            setRun(prev => ({
              ...prev,
              phase: 'done',
              genTps:    evt.result.generationSpeedTps,
              promptTps: evt.promptTps ?? evt.result.promptSpeedTps,
              promptMs:  evt.promptMs  ?? evt.result.timeToFirstTokenMs,
            }))
            queryClient.invalidateQueries({ queryKey: ['benchmarks'] })
          } else if (evt.type === 'error') {
            stopTimer()
            setRun(prev => ({ ...prev, phase: 'error', error: evt.error }))
          }
        },
        abort.signal,
        benchmarkType,
        benchmarkType === 'agentic' ? agentRounds : undefined,
        benchmarkType === 'agentic' ? agentInputTokens : undefined,
        benchmarkType === 'agentic' ? agentOutputTokens : undefined,
      )
    } catch (e: unknown) {
      if ((e as Error).name !== 'AbortError') {
        stopTimer()
        setRun(prev => ({ ...prev, phase: 'error', error: (e as Error).message }))
      }
    }
  }, [runProfileId, runNotes, nPredict, benchmarkType, agentRounds, queryClient])

  const cancelRun = async () => {
    stopTimer()
    abortRef.current?.abort()
    abortRef.current = null
    setRun(INIT_RUN)
    try { await benchmarksApi.cancel() } catch {  }
  }

  const closeProgress = () => {
    setRun(INIT_RUN)
    setRunNotes('')
  }

  const toggleSelect = (id: number) => {
    setSelected(prev => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id); else next.add(id)
      return next
    })
  }

  const latest = benchmarks?.[0]

  const chartData = benchmarks
    ? [...benchmarks].reverse().slice(-20).map(b => ({
        date: new Date(b.runAt).toLocaleDateString('en-GB', { month: 'short', day: 'numeric' }),
        gen: parseFloat(b.generationSpeedTps.toFixed(1)),
        prompt: parseFloat(b.promptSpeedTps.toFixed(1)),
        ttft: parseFloat(b.timeToFirstTokenMs.toFixed(0)),
      }))
    : []

  const isRunning = run.phase !== 'idle' && run.phase !== 'done' && run.phase !== 'error'
  const showProgress = run.phase !== 'idle'

  if (isLoading) return <div className="flex items-center justify-center h-full"><Loader2 className="animate-spin" size={32} /></div>
  if (error) return <div className="p-6 text-red-600">Failed to load benchmarks: {(error as Error).message}</div>

  return (
    <div>
    <div className="p-6 space-y-6">
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-bold text-gray-800">Benchmarks</h2>
        <div className="flex gap-2">
          <button onClick={() => setShowRunModal(true)} disabled={isRunning}
            className="flex items-center gap-1 px-3 py-1.5 bg-[#4a9eed] text-white rounded text-sm hover:bg-[#3a8edd] disabled:opacity-50">
            <Play size={14} /> Run Benchmark
          </button>
          <button onClick={async () => {
              const ids = [...selected]
              const results = await benchmarksApi.compare(ids)
              setComparing(results)
            }}
            disabled={selected.size < 2}
            className="flex items-center gap-1 px-3 py-1.5 bg-purple-600 text-white rounded text-sm hover:bg-purple-700 disabled:opacity-50">
            <BarChart3 size={14} /> Compare ({selected.size})
          </button>
          {selected.size > 0 && (
            <button onClick={async () => {
                if (!confirm(`Delete ${selected.size} benchmark${selected.size > 1 ? 's' : ''}?`)) return
                await benchmarksApi.delete([...selected])
                setSelected(new Set())
                queryClient.invalidateQueries({ queryKey: ['benchmarks'] })
              }}
              className="flex items-center gap-1 px-3 py-1.5 bg-red-500 text-white rounded text-sm hover:bg-red-600">
              <Trash2 size={14} /> Remove ({selected.size})
            </button>
          )}
          <button onClick={() => benchmarksApi.exportCsv()}
            className="flex items-center gap-1 px-3 py-1.5 bg-gray-200 text-gray-700 rounded text-sm hover:bg-gray-300">
            <Download size={14} /> Export CSV
          </button>
        </div>
      </div>

      {}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        <StatCard label="Gen Speed" value={latest ? latest.generationSpeedTps.toFixed(1) : '--'} unit="t/s" color="bg-blue-50 text-blue-700" />
        <StatCard label="Prompt Speed" value={latest ? latest.promptSpeedTps.toFixed(1) : '--'} unit="t/s" color="bg-green-50 text-green-700" />
        <StatCard label="TTFT" value={latest ? latest.timeToFirstTokenMs.toFixed(0) : '--'} unit="ms" color="bg-yellow-50 text-yellow-700" />
        <StatCard label="VRAM Used" value={latest && latest.vramUsedMb > 0 ? (latest.vramUsedMb / 1024).toFixed(1) : '--'} unit={latest && latest.vramUsedMb > 0 ? 'GB' : ''} color="bg-purple-50 text-purple-700" />
      </div>

      {}
      {chartData.length >= 2 && (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          <div className="bg-white rounded-lg shadow p-4">
            <h3 className="text-sm font-semibold text-gray-700 mb-3">Token Throughput (t/s)</h3>
            <ResponsiveContainer width="100%" height={180}>
              <LineChart data={chartData} margin={{ top: 4, right: 12, left: 0, bottom: 0 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="#f0f0f0" />
                <XAxis dataKey="date" tick={{ fontSize: 11 }} />
                <YAxis tick={{ fontSize: 11 }} />
                <Tooltip />
                <Legend iconSize={10} wrapperStyle={{ fontSize: 12 }} />
                <Line type="monotone" dataKey="gen" name="Generation" stroke="#4a9eed" strokeWidth={2} dot={{ r: 3 }} activeDot={{ r: 5 }} />
                <Line type="monotone" dataKey="prompt" name="Prompt" stroke="#22c55e" strokeWidth={2} dot={{ r: 3 }} activeDot={{ r: 5 }} />
              </LineChart>
            </ResponsiveContainer>
          </div>
          <div className="bg-white rounded-lg shadow p-4">
            <h3 className="text-sm font-semibold text-gray-700 mb-3">Time to First Token (ms)</h3>
            <ResponsiveContainer width="100%" height={180}>
              <LineChart data={chartData} margin={{ top: 4, right: 12, left: 0, bottom: 0 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="#f0f0f0" />
                <XAxis dataKey="date" tick={{ fontSize: 11 }} />
                <YAxis tick={{ fontSize: 11 }} />
                <Tooltip />
                <Line type="monotone" dataKey="ttft" name="TTFT" stroke="#f59e0b" strokeWidth={2} dot={{ r: 3 }} activeDot={{ r: 5 }} />
              </LineChart>
            </ResponsiveContainer>
          </div>
        </div>
      )}

      {}
      <div className="bg-white rounded-lg shadow overflow-hidden">
        <table className="w-full text-sm">
          <thead className="bg-gray-50 border-b">
            <tr>
              <th className="px-4 py-3 w-8"></th>
              <th className="text-left px-4 py-3 font-medium text-gray-600">Profile</th>
              <th className="text-left px-4 py-3 font-medium text-gray-600">Type</th>
              <th className="text-left px-4 py-3 font-medium text-gray-600">Date</th>
              <th className="text-right px-4 py-3 font-medium text-gray-600">Gen (t/s)</th>
              <th className="text-right px-4 py-3 font-medium text-gray-600">Prompt (t/s)</th>
              <th className="text-right px-4 py-3 font-medium text-gray-600">TTFT (ms)</th>
              <th className="text-right px-4 py-3 font-medium text-gray-600">VRAM (MB)</th>
              <th className="text-left px-4 py-3 font-medium text-gray-600">Notes</th>
            </tr>
          </thead>
          <tbody>
            {benchmarks?.map(b => (
              <tr key={b.id} onClick={() => setViewingResult(b)}
                className="border-b hover:bg-blue-50 cursor-pointer transition-colors">
                <td className="px-4 py-3" onClick={e => e.stopPropagation()}>
                  <input type="checkbox" checked={selected.has(b.id)} onChange={() => toggleSelect(b.id)} />
                </td>
                <td className="px-4 py-3 font-medium">{b.profileName}</td>
                <td className="px-4 py-3">
                  <span className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium ${
                    (b.benchmarkType ?? 'token-generation') === 'agentic'
                      ? 'bg-purple-100 text-purple-700' : 'bg-blue-100 text-blue-700'
                  }`}>
                    {(b.benchmarkType ?? 'token-generation') === 'agentic' ? 'Agentic' : 'Token Gen'}
                  </span>
                </td>
                <td className="px-4 py-3 text-gray-500">{new Date(b.runAt).toLocaleString()}</td>
                <td className="px-4 py-3 text-right font-mono">{b.generationSpeedTps.toFixed(1)}</td>
                <td className="px-4 py-3 text-right font-mono">{b.promptSpeedTps.toFixed(1)}</td>
                <td className="px-4 py-3 text-right font-mono">{b.timeToFirstTokenMs.toFixed(0)}</td>
                <td className="px-4 py-3 text-right font-mono">{b.vramUsedMb > 0 ? `${b.vramUsedMb.toFixed(0)} MB` : 'N/A'}</td>
                <td className="px-4 py-3 text-gray-500 truncate max-w-[200px]">{b.notes ?? ''}</td>
              </tr>
            ))}
            {(!benchmarks || benchmarks.length === 0) && (
              <tr><td colSpan={9} className="px-4 py-8 text-center text-gray-400">No benchmark results yet.</td></tr>
            )}
          </tbody>
        </table>
      </div>

    </div>

      {}
      {showRunModal && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-[100]">
          <div className="bg-white rounded-lg shadow-xl p-6 w-[480px] space-y-4">
            <div className="flex items-center justify-between">
              <h3 className="font-semibold text-lg">
                {modalStep === 1 ? 'Choose Benchmark Type' : 'Configure Benchmark'}
              </h3>
              <button onClick={() => { setShowRunModal(false); setModalStep(1) }} className="text-gray-400 hover:text-gray-600"><X size={18} /></button>
            </div>

            {modalStep === 1 ? (
              <div className="grid grid-cols-2 gap-3">
                <button
                  onClick={() => { setBenchmarkType('token-generation'); setModalStep(2) }}
                  className={`flex flex-col items-center gap-2 p-4 rounded-lg border-2 transition-colors hover:border-blue-400 hover:bg-blue-50 ${
                    benchmarkType === 'token-generation' ? 'border-blue-400 bg-blue-50' : 'border-gray-200'
                  }`}>
                  <Zap size={28} className="text-blue-500" />
                  <span className="font-semibold text-sm text-gray-800">Token Generation</span>
                  <span className="text-xs text-gray-500 text-center">Measures raw token generation speed, TTFT, and throughput with a single prompt.</span>
                </button>
                <button
                  onClick={() => { setBenchmarkType('agentic'); setModalStep(2) }}
                  className={`flex flex-col items-center gap-2 p-4 rounded-lg border-2 transition-colors hover:border-purple-400 hover:bg-purple-50 ${
                    benchmarkType === 'agentic' ? 'border-purple-400 bg-purple-50' : 'border-gray-200'
                  }`}>
                  <Bot size={28} className="text-purple-500" />
                  <span className="font-semibold text-sm text-gray-800">Agentic Coding Simulation</span>
                  <span className="text-xs text-gray-500 text-center">Simulates multi-turn agentic coding with tool calls across multiple rounds.</span>
                </button>
              </div>
            ) : (
              <>
                <div>
                  <label className="block text-sm text-gray-600 mb-1">Profile</label>
                  <select value={runProfileId} onChange={e => setRunProfileId(Number(e.target.value))} className="w-full border rounded px-3 py-2">
                    <option value={0}>Select profile...</option>
                    {profiles?.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
                  </select>
                </div>
                <div>
                  <label className="block text-sm text-gray-600 mb-1">Notes (optional)</label>
                  <input value={runNotes} onChange={e => setRunNotes(e.target.value)}
                    className="w-full border rounded px-3 py-2" placeholder="e.g. testing with flash-attn" />
                </div>
                {benchmarkType === 'token-generation' ? (
                  <div>
                    <label className="block text-sm text-gray-600 mb-1">
                      Tokens to generate <span className="font-semibold text-gray-800">{nPredict}</span>
                    </label>
                    <div className="flex items-center gap-2">
                      <input type="range" min={100} max={8000} step={100} value={nPredict}
                        onChange={e => setNPredict(Number(e.target.value))} className="flex-1" />
                      <input type="number" min={100} max={8000} step={100} value={nPredict}
                        onChange={e => setNPredict(Number(e.target.value))}
                        className="w-20 border rounded px-2 py-1 text-center text-sm" />
                    </div>
                  </div>
                ) : (
                  <>
                    <div>
                      <label className="block text-sm text-gray-600 mb-1">
                        Agent Rounds <span className="font-semibold text-gray-800">{agentRounds}</span>
                      </label>
                      <div className="flex items-center gap-2">
                        <input type="range" min={2} max={10} step={1} value={agentRounds}
                          onChange={e => setAgentRounds(Number(e.target.value))} className="flex-1" />
                        <input type="number" min={2} max={10} step={1} value={agentRounds}
                          onChange={e => setAgentRounds(Math.max(2, Math.min(10, Number(e.target.value))))}
                          className="w-20 border rounded px-2 py-1 text-center text-sm" />
                      </div>
                    </div>
                    <div>
                      <label className="block text-sm text-gray-600 mb-1">
                        Output tokens per round <span className="font-semibold text-gray-800">{agentOutputTokens}</span>
                      </label>
                      <div className="flex items-center gap-2">
                        <input type="range" min={64} max={2048} step={64} value={agentOutputTokens}
                          onChange={e => setAgentOutputTokens(Number(e.target.value))} className="flex-1" />
                        <input type="number" min={64} max={2048} step={64} value={agentOutputTokens}
                          onChange={e => setAgentOutputTokens(Math.max(64, Math.min(2048, Number(e.target.value))))}
                          className="w-20 border rounded px-2 py-1 text-center text-sm" />
                      </div>
                    </div>
                    <div>
                      <label className="block text-sm text-gray-600 mb-1">
                        Input tokens (approx.) <span className="font-semibold text-gray-800">{agentInputTokens}</span>
                      </label>
                      <div className="flex items-center gap-2">
                        <input type="range" min={256} max={4096} step={256} value={agentInputTokens}
                          onChange={e => setAgentInputTokens(Number(e.target.value))} className="flex-1" />
                        <input type="number" min={256} max={4096} step={256} value={agentInputTokens}
                          onChange={e => setAgentInputTokens(Math.max(256, Math.min(4096, Number(e.target.value))))}
                          className="w-20 border rounded px-2 py-1 text-center text-sm" />
                      </div>
                    </div>
                  </>
                )}
                <div className="flex gap-2 justify-between">
                  <button onClick={() => setModalStep(1)} className="flex items-center gap-1 px-4 py-2 bg-gray-200 text-gray-700 rounded text-sm hover:bg-gray-300">
                    <ArrowLeft size={14} /> Back
                  </button>
                  <div className="flex gap-2">
                    <button onClick={() => { setShowRunModal(false); setModalStep(1) }} className="px-4 py-2 bg-gray-200 text-gray-700 rounded text-sm hover:bg-gray-300">Cancel</button>
                    <button onClick={startRun} disabled={!runProfileId}
                      className="px-4 py-2 bg-[#4a9eed] text-white rounded text-sm hover:bg-[#3a8edd] disabled:opacity-50">
                      Run
                    </button>
                  </div>
                </div>
              </>
            )}
          </div>
        </div>
      )}

      {}
      {showProgress && (
        <div className="fixed inset-0 bg-black/60 flex items-center justify-center z-[100]">
          <div className="bg-white rounded-xl shadow-2xl w-[560px] overflow-hidden">
            <div className="px-6 pt-6 pb-4 flex items-center gap-3 border-b">
              <div className={`w-9 h-9 rounded-full flex items-center justify-center shrink-0 ${run.phase === 'error' ? 'bg-red-100' : 'bg-blue-100'}`}>
                {run.phase === 'done' ? <CheckCircle2 size={18} className="text-green-500" />
                  : run.phase === 'error' ? <X size={18} className="text-red-500" />
                  : <Cpu size={18} className="text-blue-600" />}
              </div>
              <div className="flex-1 min-w-0">
                <p className="font-semibold text-gray-900 truncate">
                  {run.phase === 'done' ? 'Benchmark complete' : run.phase === 'error' ? 'Benchmark failed' : 'Benchmark running'}
                </p>
                <p className="text-xs text-gray-400 truncate">
                  {profileName}{benchmarkType === 'agentic' ? ` · ${agentRounds} rounds · ~${agentInputTokens}in / ${agentOutputTokens}out` : ` · ${run.nPredict} tokens`}
                </p>
              </div>
              <span className="text-xl font-mono font-bold text-gray-700 shrink-0">{fmtElapsed(run.elapsed)}</span>
            </div>

            {isRunning && (
              <div className="h-1 w-full overflow-hidden">
                <div className="h-1 w-full animate-shimmer"
                  style={{ background: 'linear-gradient(90deg,#93c5fd 0%,#4a9eed 40%,#93c5fd 100%)', backgroundSize: '200% 100%' }} />
              </div>
            )}

            <div className="px-6 py-4 space-y-4">
              {}
              <div className="flex gap-2 flex-wrap">
                {(['starting', 'loading', 'generating'] as const).map(p => {
                  const phases = ['starting', 'loading', 'generating', 'done']
                  const done   = phases.indexOf(run.phase) > phases.indexOf(p)
                  const active = run.phase === p
                  return (
                    <span key={p} className={`flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-medium
                      ${done ? 'bg-green-100 text-green-700' : active ? 'bg-blue-100 text-blue-700' : 'bg-gray-100 text-gray-400'}`}>
                      {done ? <CheckCircle2 size={11} /> : active ? <Loader2 size={11} className="animate-spin" /> : <span className="w-2 h-2 rounded-full bg-gray-300 inline-block" />}
                      {PHASE_LABELS[p]}
                    </span>
                  )
                })}
              </div>

              {}
              {(run.phase === 'generating' || run.phase === 'done') && (
                <div className="flex gap-3 text-xs">
                  <div className="flex-1 bg-blue-50 rounded px-2 py-1.5">
                    <span className="text-blue-400">Gen speed</span>
                    <span className="ml-2 font-mono font-bold text-blue-700">{run.genTps} t/s</span>
                  </div>
                  <div className="flex-1 bg-gray-50 rounded px-2 py-1.5">
                    <span className="text-gray-400">Tokens</span>
                    <span className="ml-2 font-mono font-bold text-gray-700">
                      {run.tokenCount}{benchmarkType !== 'agentic' && ` / ${run.nPredict}`}
                    </span>
                  </div>
                  {run.promptTps > 0 ? (
                    <div className="flex-1 bg-green-50 rounded px-2 py-1.5">
                      <span className="text-green-400">Prompt</span>
                      <span className="ml-2 font-mono font-bold text-green-700">{run.promptTps.toFixed(1)} t/s</span>
                    </div>
                  ) : run.promptMs > 0 ? (
                    <div className="flex-1 bg-yellow-50 rounded px-2 py-1.5">
                      <span className="text-yellow-500">TTFT</span>
                      <span className="ml-2 font-mono font-bold text-yellow-700">~{run.promptMs.toFixed(0)} ms</span>
                    </div>
                  ) : null}
                </div>
              )}

              {}
              {run.liveRounds.length > 0 && (
                <div className="space-y-2">
                  <p className="text-xs font-medium text-gray-500">Agent Rounds</p>
                  {run.liveRounds.map(r => (
                    <div key={r.round} className="flex items-center gap-2 bg-purple-50 rounded px-3 py-1.5 text-xs">
                      <CheckCircle2 size={12} className="text-purple-500 shrink-0" />
                      <span className="font-medium text-purple-700">Round {r.round}/{r.totalRounds}</span>
                      <span className="text-purple-500 ml-auto font-mono">{r.speedTps.toFixed(1)} t/s</span>
                      <span className="text-purple-400 font-mono">{r.ttftMs.toFixed(0)} ms TTFT</span>
                      <span className="text-purple-400 font-mono">{r.outputTokens} out / {r.inputTokens} ctx</span>
                    </div>
                  ))}
                </div>
              )}

              {}
              {run.genPoints.length >= 1 ? (
                <div className="grid grid-cols-2 gap-3">
                  {}
                  <div>
                    <div className="flex items-center justify-between mb-1">
                      <p className="text-xs font-medium text-gray-500">Generation speed</p>
                      <span className="text-xs font-mono font-semibold text-blue-600">{run.genTps} t/s</span>
                    </div>
                    <ResponsiveContainer width="100%" height={110}>
                      <LineChart data={run.genPoints} margin={{ top: 2, right: 4, left: 0, bottom: 2 }}>
                        <CartesianGrid strokeDasharray="2 2" stroke="#f0f0f0" vertical={false} />
                        <XAxis dataKey="timeS" tick={{ fontSize: 9 }} tickLine={false} axisLine={false}
                          tickFormatter={(v: number) => `${v}s`} />
                        <YAxis tick={{ fontSize: 9 }} tickLine={false} axisLine={false} width={30} />
                        <Tooltip contentStyle={{ fontSize: 11 }}
                          formatter={(v: unknown) => [`${v} t/s`, 'Gen speed']}
                          labelFormatter={(v: unknown) => `${v}s elapsed`} />
                        {run.genPoints.length >= 3 && (
                          <ReferenceLine
                            y={Math.round(run.genPoints.reduce((s, p) => s + p.tps, 0) / run.genPoints.length)}
                            stroke="#94a3b8" strokeDasharray="3 3" strokeWidth={1} />
                        )}
                        <Line type="monotone" dataKey="tps" stroke="#4a9eed" strokeWidth={2} dot={false} isAnimationActive={false} />
                      </LineChart>
                    </ResponsiveContainer>
                  </div>

                  {}
                  <div>
                    <div className="flex items-center justify-between mb-1">
                      <p className="text-xs font-medium text-gray-500">Prompt processing</p>
                      {run.promptTps > 0 && (
                        <span className="text-xs font-mono font-semibold text-green-600">{run.promptTps.toFixed(1)} t/s</span>
                      )}
                    </div>
                    {run.promptTps > 0 ? (
                      <ResponsiveContainer width="100%" height={110}>
                        <LineChart data={run.genPoints.map(pt => ({ timeS: pt.timeS, tps: run.promptTps }))} margin={{ top: 2, right: 4, left: 0, bottom: 2 }}>
                          <CartesianGrid strokeDasharray="2 2" stroke="#f0f0f0" vertical={false} />
                          <XAxis dataKey="timeS" tick={{ fontSize: 9 }} tickLine={false} axisLine={false}
                            tickFormatter={(v: number) => `${v}s`} />
                          <YAxis tick={{ fontSize: 9 }} tickLine={false} axisLine={false} width={30} />
                          <Tooltip contentStyle={{ fontSize: 11 }}
                            formatter={(v: unknown) => [`${v} t/s`, 'Prompt speed']}
                            labelFormatter={(v: unknown) => `${v}s elapsed`} />
                          <Line type="monotone" dataKey="tps" stroke="#22c55e" strokeWidth={2} dot={false} isAnimationActive={false} />
                        </LineChart>
                      </ResponsiveContainer>
                    ) : (
                      <div className="flex items-center justify-center" style={{ height: 110 }}>
                        {run.promptMs > 0 ? (
                          <div className="text-center">
                            <p className="text-xs font-semibold text-yellow-700">Time to first token</p>
                            <p className="text-lg font-mono font-bold text-yellow-600">~{run.promptMs.toFixed(0)} ms</p>
                          </div>
                        ) : run.promptProgress > 0 ? (
                          <div className="w-full px-2">
                            <p className="text-xs text-gray-500 mb-1.5">Processing prompt...</p>
                            <div className="w-full bg-gray-100 rounded-full h-2.5 overflow-hidden">
                              <div
                                className="h-2.5 rounded-full transition-all duration-200"
                                style={{ width: `${run.promptProgress}%`, background: 'linear-gradient(90deg,#22c55e,#4a9eed)' }}
                              />
                            </div>
                            <p className="text-xs font-mono text-gray-500 mt-1 text-right">{run.promptProgress.toFixed(1)}%</p>
                          </div>
                        ) : (
                          <p className="text-xs text-gray-400">Waiting...</p>
                        )}
                      </div>
                    )}
                  </div>
                </div>
              ) : (
                run.phase !== 'done' && run.phase !== 'error' && (
                  <p className="text-xs text-gray-400 text-center py-4">
                    {run.phase === 'generating' ? 'Collecting first data point…' : 'Charts appear once generation starts.'}
                  </p>
                )
              )}

              {}
              {run.phase === 'error' && run.error && (
                <div className="bg-red-50 border border-red-200 rounded-lg px-3 py-2">
                  <p className="text-xs font-medium text-red-700">Error</p>
                  <p className="text-xs text-red-600 mt-0.5">{run.error}</p>
                </div>
              )}

              {}
              {run.phase === 'done' && (
                <div className="flex gap-3">
                  <MiniStat label="Gen speed"    value={`${run.genTps} t/s`}              color="text-blue-700" />
                  {run.promptTps > 0 && <MiniStat label="Prompt speed" value={`${run.promptTps.toFixed(1)} t/s`} color="text-green-700" />}
                  {run.promptMs  > 0 && <MiniStat label="TTFT"         value={`${run.promptMs.toFixed(0)} ms`}   color="text-yellow-700" />}
                </div>
              )}
            </div>

            <div className="px-6 pb-5 flex justify-end gap-2">
              {isRunning && (
                <button onClick={cancelRun} className="px-4 py-2 bg-gray-200 text-gray-700 rounded text-sm hover:bg-gray-300">Cancel</button>
              )}
              {(run.phase === 'done' || run.phase === 'error') && (
                <button onClick={closeProgress} className="px-4 py-2 bg-[#4a9eed] text-white rounded text-sm hover:bg-[#3a8edd]">Done</button>
              )}
            </div>
          </div>
        </div>
      )}

      {}
      {viewingResult && (
        <div className="fixed inset-0 bg-black/60 flex items-center justify-center z-[100]"
          onClick={() => setViewingResult(null)}>
          <div className="bg-white rounded-xl shadow-2xl w-[540px] p-6 space-y-4" onClick={e => e.stopPropagation()}>
            <div className="flex items-center justify-between">
              <div>
                <div className="flex items-center gap-2">
                  <h3 className="font-semibold text-lg">{viewingResult.profileName}</h3>
                  <span className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium ${
                    (viewingResult.benchmarkType ?? 'token-generation') === 'agentic'
                      ? 'bg-purple-100 text-purple-700' : 'bg-blue-100 text-blue-700'
                  }`}>
                    {(viewingResult.benchmarkType ?? 'token-generation') === 'agentic' ? 'Agentic' : 'Token Gen'}
                  </span>
                </div>
                <p className="text-xs text-gray-400">
                  {new Date(viewingResult.runAt).toLocaleString()}
                  {viewingResult.notes ? ` · ${viewingResult.notes}` : ''}
                </p>
              </div>
              <button onClick={() => setViewingResult(null)} className="text-gray-400 hover:text-gray-600"><X size={18} /></button>
            </div>

            <div className="flex gap-3">
              <MiniStat label="Gen speed" value={`${viewingResult.generationSpeedTps.toFixed(1)} t/s`} color="text-blue-700" />
              <MiniStat label="Prompt speed" value={`${viewingResult.promptSpeedTps.toFixed(1)} t/s`} color="text-green-700" />
              <MiniStat label="TTFT" value={`${viewingResult.timeToFirstTokenMs.toFixed(0)} ms`} color="text-yellow-700" />
            </div>

            {}
            {(viewingResult.benchmarkType ?? 'token-generation') === 'agentic' && viewingResult.rounds && viewingResult.rounds.length > 0 && (
              <div>
                <p className="text-xs font-medium text-gray-500 mb-2">Agent Rounds</p>
                <div className="overflow-x-auto">
                  <table className="w-full text-xs border-collapse">
                    <thead>
                      <tr className="bg-gray-50">
                        <th className="text-left px-3 py-1.5 font-medium text-gray-500 border-b">Round</th>
                        <th className="text-right px-3 py-1.5 font-medium text-gray-500 border-b">Input Tokens</th>
                        <th className="text-right px-3 py-1.5 font-medium text-gray-500 border-b">Output Tokens</th>
                        <th className="text-right px-3 py-1.5 font-medium text-gray-500 border-b">TTFT (ms)</th>
                        <th className="text-right px-3 py-1.5 font-medium text-gray-500 border-b">Speed (TPS)</th>
                      </tr>
                    </thead>
                    <tbody>
                      {viewingResult.rounds.map((r: AgentRoundResult) => (
                        <tr key={r.round} className="border-b last:border-0">
                          <td className="px-3 py-1.5 font-medium">{r.round}</td>
                          <td className="px-3 py-1.5 text-right font-mono">{r.inputTokens}</td>
                          <td className="px-3 py-1.5 text-right font-mono">{r.outputTokens}</td>
                          <td className="px-3 py-1.5 text-right font-mono">{r.ttftMs.toFixed(0)}</td>
                          <td className="px-3 py-1.5 text-right font-mono">{r.speedTps.toFixed(1)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>
            )}

            {viewingResult.chartDataJson ? (() => {
              const pts: LivePoint[] = JSON.parse(viewingResult.chartDataJson)
              return pts.length > 1 ? (
                <div className="grid grid-cols-2 gap-3">
                  {}
                  <div>
                    <p className="text-xs font-medium text-gray-500 mb-2">Generation speed over time</p>
                    <ResponsiveContainer width="100%" height={140}>
                      <LineChart data={pts} margin={{ top: 2, right: 8, left: 0, bottom: 2 }}>
                        <CartesianGrid strokeDasharray="3 3" stroke="#f0f0f0" />
                        <XAxis dataKey="n" tick={{ fontSize: 9 }} tickLine={false} />
                        <YAxis tick={{ fontSize: 9 }} width={30} />
                        <Tooltip formatter={(v: unknown) => [`${v} t/s`, 'Gen']} labelFormatter={(n: unknown) => `token ${n}`} />
                        {pts.length > 4 && (
                          <ReferenceLine y={Math.round(pts.reduce((s, p) => s + p.tps, 0) / pts.length)} stroke="#94a3b8" strokeDasharray="3 3" strokeWidth={1} />
                        )}
                        <Line type="monotone" dataKey="tps" stroke="#4a9eed" strokeWidth={2} dot={false} isAnimationActive={false} />
                      </LineChart>
                    </ResponsiveContainer>
                  </div>
                  {}
                  <div>
                    <p className="text-xs font-medium text-gray-500 mb-2">Prompt processing</p>
                    <ResponsiveContainer width="100%" height={140}>
                      <BarChart data={[{ name: 'Prompt', tps: viewingResult.promptSpeedTps }]} margin={{ top: 2, right: 8, left: 0, bottom: 2 }}>
                        <XAxis dataKey="name" tick={{ fontSize: 9 }} axisLine={false} tickLine={false} />
                        <YAxis tick={{ fontSize: 9 }} axisLine={false} tickLine={false} width={30} />
                        <Tooltip formatter={(v: unknown) => [`${v} t/s`, 'Prompt']} />
                        <Bar dataKey="tps" fill="#22c55e" radius={[4, 4, 0, 0]} />
                      </BarChart>
                    </ResponsiveContainer>
                    <p className="text-xs text-gray-400 text-right">{viewingResult.timeToFirstTokenMs.toFixed(0)} ms TTFT</p>
                  </div>
                </div>
              ) : <p className="text-sm text-gray-400 text-center py-4">Not enough data points to chart.</p>
            })() : (
              <p className="text-sm text-gray-400 text-center py-4">No chart data — run a new benchmark to capture it.</p>
            )}

            <div className="flex justify-end">
              <button onClick={() => setViewingResult(null)} className="px-4 py-2 bg-gray-200 text-gray-700 rounded text-sm hover:bg-gray-300">Close</button>
            </div>
          </div>
        </div>
      )}

      {}
      {comparing && (
        <div className="fixed inset-0 bg-black/60 flex items-center justify-center z-[100]"
          onClick={() => setComparing(null)}>
          <div className="bg-white rounded-xl shadow-2xl w-[700px] max-h-[90vh] overflow-y-auto p-6 space-y-5"
            onClick={e => e.stopPropagation()}>

            {}
            <div className="flex items-center justify-between">
              <h3 className="font-semibold text-lg">Comparing {comparing.length} benchmarks</h3>
              <button onClick={() => setComparing(null)} className="text-gray-400 hover:text-gray-600">
                <X size={18} />
              </button>
            </div>

            {}
            {(() => {
              const COLORS = ['#4a9eed', '#f59e0b', '#22c55e', '#a855f7', '#ef4444']
              const barData = [
                { metric: 'Gen Speed (t/s)', ...Object.fromEntries(comparing.map((b, i) => [b.profileName + (i > 0 ? ` (${i+1})` : ''), b.generationSpeedTps])) },
                { metric: 'Prompt Speed (t/s)', ...Object.fromEntries(comparing.map((b, i) => [b.profileName + (i > 0 ? ` (${i+1})` : ''), b.promptSpeedTps])) },
                { metric: 'TTFT (ms)', ...Object.fromEntries(comparing.map((b, i) => [b.profileName + (i > 0 ? ` (${i+1})` : ''), b.timeToFirstTokenMs])) },
              ]
              const keys = comparing.map((b, i) => b.profileName + (i > 0 ? ` (${i+1})` : ''))
              return (
                <div>
                  <p className="text-xs font-medium text-gray-500 mb-2">Performance metrics</p>
                  <ResponsiveContainer width="100%" height={200}>
                    <BarChart data={barData} margin={{ top: 4, right: 8, left: 0, bottom: 4 }}>
                      <CartesianGrid strokeDasharray="3 3" stroke="#f0f0f0" vertical={false} />
                      <XAxis dataKey="metric" tick={{ fontSize: 11 }} axisLine={false} tickLine={false} />
                      <YAxis tick={{ fontSize: 11 }} axisLine={false} tickLine={false} width={40} />
                      <Tooltip contentStyle={{ fontSize: 11 }} />
                      <Legend iconSize={10} wrapperStyle={{ fontSize: 11 }} />
                      {keys.map((key, i) => (
                        <Bar key={key} dataKey={key} fill={COLORS[i % COLORS.length]} radius={[3, 3, 0, 0]} />
                      ))}
                    </BarChart>
                  </ResponsiveContainer>
                </div>
              )
            })()}

            {}
            {comparing.some(b => b.chartDataJson) && (() => {
              const COLORS = ['#4a9eed', '#f59e0b', '#22c55e', '#a855f7', '#ef4444']
              const allPts = comparing.map((b, i) => ({
                name: b.profileName + (i > 0 ? ` (${i+1})` : ''),
                color: COLORS[i % COLORS.length],
                pts: b.chartDataJson ? JSON.parse(b.chartDataJson) as { n: number; tps: number }[] : [],
              })).filter(s => s.pts.length > 0)

              if (allPts.length === 0) return null

              const allN = [...new Set(allPts.flatMap(s => s.pts.map(p => p.n)))].sort((a, b) => a - b)
              const merged = allN.map(n => {
                const row: Record<string, number> = { n }
                for (const series of allPts) {
                  const pt = series.pts.find(p => p.n === n)
                  if (pt) row[series.name] = pt.tps
                }
                return row
              })

              return (
                <div>
                  <p className="text-xs font-medium text-gray-500 mb-2">Generation speed over time</p>
                  <ResponsiveContainer width="100%" height={160}>
                    <LineChart data={merged} margin={{ top: 2, right: 8, left: 0, bottom: 2 }}>
                      <CartesianGrid strokeDasharray="3 3" stroke="#f0f0f0" />
                      <XAxis dataKey="n" tick={{ fontSize: 9 }} tickLine={false} axisLine={false}
                        tickFormatter={(v: number) => `${v}`} label={{ value: 'tokens', position: 'insideBottomRight', offset: -4, fontSize: 9 }} />
                      <YAxis tick={{ fontSize: 9 }} tickLine={false} axisLine={false} width={30}
                        label={{ value: 't/s', angle: -90, position: 'insideLeft', fontSize: 9 }} />
                      <Tooltip contentStyle={{ fontSize: 11 }}
                        formatter={(v: unknown, name?: string | number) => [`${v} t/s`, `${name ?? ''}`]}
                        labelFormatter={(n: unknown) => `token ${n}`} />
                      <Legend iconSize={10} wrapperStyle={{ fontSize: 11 }} />
                      {allPts.map(s => (
                        <Line key={s.name} type="monotone" dataKey={s.name}
                          stroke={s.color} strokeWidth={2} dot={false} isAnimationActive={false}
                          connectNulls />
                      ))}
                    </LineChart>
                  </ResponsiveContainer>
                </div>
              )
            })()}

            {}
            <div>
              <p className="text-xs font-medium text-gray-500 mb-2">Details</p>
              <div className="overflow-x-auto">
                <table className="w-full text-sm border-collapse">
                  <thead>
                    <tr className="bg-gray-50">
                      <th className="text-left px-3 py-2 text-xs font-medium text-gray-500 border-b">Metric</th>
                      {comparing.map((b, i) => (
                        <th key={b.id} className="text-right px-3 py-2 text-xs font-medium text-gray-700 border-b">
                          {b.profileName}{i > 0 ? ` (${i+1})` : ''}
                          <span className="block text-gray-400 font-normal">{new Date(b.runAt).toLocaleDateString()}</span>
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {[
                      { label: 'Gen Speed', fmt: (b: BenchmarkResult) => `${b.generationSpeedTps.toFixed(1)} t/s` },
                      { label: 'Prompt Speed', fmt: (b: BenchmarkResult) => `${b.promptSpeedTps.toFixed(1)} t/s` },
                      { label: 'TTFT', fmt: (b: BenchmarkResult) => `${b.timeToFirstTokenMs.toFixed(0)} ms` },
                      { label: 'VRAM', fmt: (b: BenchmarkResult) => b.vramUsedMb > 0 ? `${b.vramUsedMb.toFixed(0)} MB` : 'N/A' },
                      { label: 'Notes', fmt: (b: BenchmarkResult) => b.notes ?? '—' },
                    ].map(row => (
                      <tr key={row.label} className="border-b last:border-0">
                        <td className="px-3 py-2 text-xs text-gray-500">{row.label}</td>
                        {comparing.map(b => (
                          <td key={b.id} className="px-3 py-2 text-right font-mono text-xs">{row.fmt(b)}</td>
                        ))}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>

            <div className="flex justify-end">
              <button onClick={() => setComparing(null)}
                className="px-4 py-2 bg-gray-200 text-gray-700 rounded text-sm hover:bg-gray-300">Close</button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

function StatCard({ label, value, unit, color }: { label: string; value: string; unit: string; color: string }) {
  return (
    <div className={`rounded-lg p-4 ${color}`}>
      <p className="text-sm opacity-75">{label}</p>
      <p className="text-2xl font-bold">{value} <span className="text-sm font-normal">{unit}</span></p>
    </div>
  )
}

function MiniStat({ label, value, color }: { label: string; value: string; color: string }) {
  return (
    <div className="flex-1 bg-gray-50 rounded-lg px-3 py-2">
      <p className="text-xs text-gray-400">{label}</p>
      <p className={`text-sm font-semibold font-mono ${color}`}>{value}</p>
    </div>
  )
}
