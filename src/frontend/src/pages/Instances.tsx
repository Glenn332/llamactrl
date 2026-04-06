import { useState, useEffect, useRef, useCallback } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { instancesApi } from '../api/instances'
import { profilesApi } from '../api/profiles'
import { StatusBadge } from '../components/StatusBadge'
import { useMetricsHub, useLogHub } from '../hooks/useSignalR'
import type { Instance } from '../api/types'
import { Play, Square, Pencil, ScrollText, Plus, RefreshCw, StopCircle, Loader2, X, ExternalLink } from 'lucide-react'

function formatUptime(updatedAt: string): string {
  const utcString = updatedAt.endsWith('Z') ? updatedAt : updatedAt + 'Z'
  const elapsed = Math.floor((Date.now() - new Date(utcString).getTime()) / 1000)
  if (elapsed < 0) return '0s'
  const h = Math.floor(elapsed / 3600)
  const m = Math.floor((elapsed % 3600) / 60)
  const s = elapsed % 60
  if (h > 0) return `${h}h ${m}m ${s}s`
  if (m > 0) return `${m}m ${s}s`
  return `${s}s`
}

export function Instances() {
  const queryClient = useQueryClient()
  const [selectedId, setSelectedId] = useState<number | null>(null)
  const [showForm, setShowForm] = useState(false)
  const [formData, setFormData] = useState({ name: '', profileId: 0, port: 8080 })
  const [logsInstanceId, setLogsInstanceId] = useState<number | null>(null)
  const [initialLogs, setInitialLogs] = useState<string[]>([])
  const [editingInstance, setEditingInstance] = useState<Instance | null>(null)
  const [editForm, setEditForm] = useState({ name: '', port: 0 })
  const logsEndRef = useRef<HTMLDivElement>(null)
  const [, setTick] = useState(0)
  const { logs: liveLogs, clearLogs } = useLogHub(logsInstanceId)
  const allLogs = [...initialLogs, ...liveLogs]

  const { data: instances, isLoading, error } = useQuery({
    queryKey: ['instances'],
    queryFn: instancesApi.getAll,
    refetchInterval: selectedId ? 5000 : false,
  })

  const hasRunning = instances?.some(i => i.status === 'Running' || i.status === 'Starting')
  useEffect(() => {
    if (!hasRunning) return
    const id = setInterval(() => setTick(t => t + 1), 1000)
    return () => clearInterval(id)
  }, [hasRunning])

  const { data: profiles } = useQuery({
    queryKey: ['profiles'],
    queryFn: profilesApi.getAll,
  })

  useMetricsHub((id, status) => {
    queryClient.setQueryData<Instance[]>(['instances'], old =>
      old?.map(inst => inst.id === id ? { ...inst, status: status as Instance['status'] } : inst)
    )
  })

  const startMut = useMutation({
    mutationFn: instancesApi.start,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['instances'] }),
  })
  const stopMut = useMutation({
    mutationFn: instancesApi.stop,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['instances'] }),
  })
  const createMut = useMutation({
    mutationFn: instancesApi.create,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['instances'] })
      setShowForm(false)
      setFormData({ name: '', profileId: 0, port: 8080 })
    },
  })
  const updateMut = useMutation({
    mutationFn: ({ id, data }: { id: number; data: { name: string; port: number } }) =>
      instancesApi.update(id, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['instances'] })
      setEditingInstance(null)
    },
  })
  const deleteMut = useMutation({
    mutationFn: instancesApi.delete,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['instances'] })
      setSelectedId(null)
    },
  })
  const stopAllMut = useMutation({
    mutationFn: async () => {
      const running = instances?.filter(i => i.status === 'Running') ?? []
      await Promise.all(running.map(i => instancesApi.stop(i.id)))
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['instances'] }),
  })

  const selected = instances?.find(i => i.id === selectedId)

  useEffect(() => {
    if (!logsInstanceId) { setInitialLogs([]); return }
    instancesApi.getLogs(logsInstanceId).then(r => setInitialLogs(r.lines)).catch(() => {})
  }, [logsInstanceId])

  useEffect(() => {
    logsEndRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [allLogs.length])

  if (isLoading) return <div className="flex items-center justify-center h-full"><Loader2 className="animate-spin" size={32} /></div>
  if (error) return <div className="p-6 text-red-600">Failed to load instances: {(error as Error).message}</div>

  return (
    <div className="p-6 space-y-4">
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-bold text-gray-800">Instances</h2>
        <div className="flex gap-2">
          <button onClick={() => setShowForm(true)} className="flex items-center gap-1 px-3 py-1.5 bg-[#4a9eed] text-white rounded text-sm hover:bg-[#3a8edd] transition-colors">
            <Plus size={14} /> New Instance
          </button>
          <button onClick={() => stopAllMut.mutate()} className="flex items-center gap-1 px-3 py-1.5 bg-red-600 text-white rounded text-sm hover:bg-red-700 transition-colors">
            <StopCircle size={14} /> Stop All
          </button>
          <button onClick={() => queryClient.invalidateQueries({ queryKey: ['instances'] })} className="flex items-center gap-1 px-3 py-1.5 bg-gray-200 text-gray-700 rounded text-sm hover:bg-gray-300 transition-colors">
            <RefreshCw size={14} /> Refresh
          </button>
        </div>
      </div>

      {showForm && (
        <div className="bg-white rounded-lg shadow p-4 space-y-3">
          <h3 className="font-semibold">Create New Instance</h3>
          <div className="grid grid-cols-3 gap-3">
            <input placeholder="Instance name" value={formData.name} onChange={e => setFormData(d => ({ ...d, name: e.target.value }))}
              className="border rounded px-3 py-1.5 text-sm" />
            <select value={formData.profileId} onChange={e => setFormData(d => ({ ...d, profileId: Number(e.target.value) }))}
              className="border rounded px-3 py-1.5 text-sm">
              <option value={0}>Select profile...</option>
              {profiles?.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
            </select>
            <input type="number" placeholder="Port" value={formData.port} onChange={e => setFormData(d => ({ ...d, port: Number(e.target.value) }))}
              className="border rounded px-3 py-1.5 text-sm" />
          </div>
          <div className="flex gap-2">
            <button onClick={() => createMut.mutate(formData)} disabled={!formData.name || !formData.profileId}
              className="px-3 py-1.5 bg-[#4a9eed] text-white rounded text-sm hover:bg-[#3a8edd] disabled:opacity-50">Create</button>
            <button onClick={() => setShowForm(false)} className="px-3 py-1.5 bg-gray-200 text-gray-700 rounded text-sm hover:bg-gray-300">Cancel</button>
          </div>
        </div>
      )}

      <div className="bg-white rounded-lg shadow overflow-hidden">
        <table className="w-full text-sm">
          <thead className="bg-gray-50 border-b">
            <tr>
              <th className="text-left px-4 py-3 font-medium text-gray-600">Name</th>
              <th className="text-left px-4 py-3 font-medium text-gray-600">Model</th>
              <th className="text-left px-4 py-3 font-medium text-gray-600">Port</th>
              <th className="text-left px-4 py-3 font-medium text-gray-600">Status</th>
              <th className="text-left px-4 py-3 font-medium text-gray-600">Actions</th>
            </tr>
          </thead>
          <tbody>
            {instances?.map(inst => (
              <tr key={inst.id} onClick={() => setSelectedId(inst.id)}
                className={`border-b cursor-pointer hover:bg-blue-50 transition-colors ${selectedId === inst.id ? 'bg-blue-50' : ''}`}>
                <td className="px-4 py-3 font-medium">{inst.name}</td>
                <td className="px-4 py-3 text-gray-600">{inst.profileName}</td>
                <td className="px-4 py-3 text-gray-600">{inst.port}</td>
                <td className="px-4 py-3"><StatusBadge status={inst.status} /></td>
                <td className="px-4 py-3">
                  <div className="flex gap-1" onClick={e => e.stopPropagation()}>
                    {inst.status === 'Running' ? (
                      <>
                        <button onClick={() => stopMut.mutate(inst.id)} title="Stop"
                          className="p-1 text-red-600 hover:bg-red-50 rounded"><Square size={14} /></button>
                        <a href={`http://localhost:${inst.port}`} target="_blank" rel="noreferrer" title="Open in browser"
                          className="p-1 text-gray-600 hover:bg-gray-100 rounded inline-flex items-center"><ExternalLink size={14} /></a>
                      </>
                    ) : (
                      <button onClick={() => startMut.mutate(inst.id)} title="Start"
                        className="p-1 text-green-600 hover:bg-green-50 rounded"><Play size={14} /></button>
                    )}
                    <button title="Edit" onClick={() => { setEditingInstance(inst); setEditForm({ name: inst.name, port: inst.port }) }}
                      className="p-1 text-blue-600 hover:bg-blue-50 rounded"><Pencil size={14} /></button>
                    <button title="Logs" onClick={() => { setLogsInstanceId(prev => prev === inst.id ? null : inst.id); clearLogs() }}
                      className={`p-1 rounded ${logsInstanceId === inst.id ? 'text-blue-600 bg-blue-50' : 'text-gray-600 hover:bg-gray-100'}`}>
                      <ScrollText size={14} />
                    </button>
                  </div>
                </td>
              </tr>
            ))}
            {(!instances || instances.length === 0) && (
              <tr><td colSpan={5} className="px-4 py-8 text-center text-gray-400">No instances yet. Create one to get started.</td></tr>
            )}
          </tbody>
        </table>
      </div>

      {logsInstanceId && (
        <div className="bg-white rounded-lg shadow overflow-hidden">
          <div className="flex items-center justify-between px-4 py-2 border-b">
            <p className="text-sm font-semibold">
              {instances?.find(i => i.id === logsInstanceId)?.name ?? ''} — Logs
            </p>
            <div className="flex gap-2">
              <button onClick={() => { setInitialLogs([]); clearLogs() }}
                className="text-xs text-gray-500 hover:text-gray-700 px-2 py-1 bg-gray-100 rounded">Clear</button>
              <button onClick={() => setLogsInstanceId(null)}
                className="text-gray-400 hover:text-gray-600 p-1"><X size={14} /></button>
            </div>
          </div>
          <div className="h-64 overflow-y-auto bg-gray-900 p-3">
            {allLogs.length === 0
              ? <p className="text-gray-500 text-xs font-mono">No logs yet...</p>
              : allLogs.map((line, i) => (
                  <p key={i} className={`text-xs font-mono whitespace-pre-wrap break-all leading-5
                    ${line.includes('[ERR]') || line.includes('error') ? 'text-red-400'
                      : line.includes('WARN') || line.includes('warn') ? 'text-yellow-400'
                      : 'text-gray-200'}`}>{line}</p>
                ))
            }
            <div ref={logsEndRef} />
          </div>
        </div>
      )}

      {selected && (
        <div className="bg-white rounded-lg shadow p-4 space-y-3">
          <div className="flex items-center justify-between">
            <h3 className="font-semibold text-lg">{selected.name}</h3>
            <button onClick={() => deleteMut.mutate(selected.id)} className="px-3 py-1 bg-red-100 text-red-700 rounded text-sm hover:bg-red-200">Delete</button>
          </div>
          <div className="grid grid-cols-2 md:grid-cols-4 gap-4 text-sm">
            <div>
              <span className="text-gray-500">PID</span>
              <p className="font-mono">{selected.pid ?? '--'}</p>
            </div>
            <div>
              <span className="text-gray-500">Uptime</span>
              <p className="font-mono">{selected.status === 'Running' ? formatUptime(selected.updatedAt) : '--'}</p>
            </div>
            <div>
              <span className="text-gray-500">Tokens/sec</span>
              <p className="font-mono">{selected.metrics && selected.metrics.tokensPerSec > 0 ? selected.metrics.tokensPerSec.toFixed(1) : '--'}</p>
            </div>
            <div>
              <span className="text-gray-500">Avg Latency</span>
              <p className="font-mono">{selected.metrics && selected.metrics.avgLatencyMs > 0 ? `${selected.metrics.avgLatencyMs.toFixed(0)}ms` : '--'}</p>
            </div>
            <div>
              <span className="text-gray-500">Total Requests</span>
              <p className="font-mono">{selected.metrics ? selected.metrics.totalRequests : '--'}</p>
            </div>
            <div>
              <span className="text-gray-500">Profile</span>
              <p>{selected.profileName}</p>
            </div>
          </div>
          <div className="flex gap-2 pt-2">
            <button className="px-3 py-1.5 bg-[#4a9eed] text-white rounded text-sm hover:bg-[#3a8edd]">Open Profile</button>
            <button className="px-3 py-1.5 bg-purple-600 text-white rounded text-sm hover:bg-purple-700">Run Benchmark</button>
          </div>
        </div>
      )}

      {editingInstance && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-[100]"
          onClick={() => setEditingInstance(null)}>
          <div className="bg-white rounded-lg shadow-xl p-6 w-96 space-y-4" onClick={e => e.stopPropagation()}>
            <div className="flex items-center justify-between">
              <h3 className="font-semibold text-lg">Edit Instance</h3>
              <button onClick={() => setEditingInstance(null)} className="text-gray-400 hover:text-gray-600">
                <X size={18} />
              </button>
            </div>
            <div className="space-y-3">
              <div>
                <label className="block text-sm text-gray-600 mb-1">Name</label>
                <input value={editForm.name} onChange={e => setEditForm(f => ({ ...f, name: e.target.value }))}
                  className="w-full border rounded px-3 py-2 text-sm" />
              </div>
              <div>
                <label className="block text-sm text-gray-600 mb-1">Port</label>
                <input type="number" value={editForm.port} onChange={e => setEditForm(f => ({ ...f, port: Number(e.target.value) }))}
                  className="w-full border rounded px-3 py-2 text-sm" />
                {editingInstance.status === 'Running' && (
                  <p className="text-xs text-amber-600 mt-1">Restart required to apply port changes.</p>
                )}
              </div>
            </div>
            <div className="flex gap-2 justify-end">
              <button onClick={() => setEditingInstance(null)}
                className="px-4 py-2 bg-gray-200 text-gray-700 rounded text-sm hover:bg-gray-300">Cancel</button>
              <button onClick={() => updateMut.mutate({ id: editingInstance.id, data: editForm })}
                disabled={!editForm.name || updateMut.isPending}
                className="px-4 py-2 bg-[#4a9eed] text-white rounded text-sm hover:bg-[#3a8edd] disabled:opacity-50">
                {updateMut.isPending ? 'Saving...' : 'Save'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
