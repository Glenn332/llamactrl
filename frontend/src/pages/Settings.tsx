import { useState, useEffect, useRef } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { settingsApi } from '../api/settings'
import { binariesApi } from '../api/binaries'
import { modelDirectoriesApi } from '../api/modelDirectories'
import type { AppSettings, LlamaServerBinary, ModelDirectory } from '../api/types'
import { Loader2, Save, Upload, Download, RotateCcw, Info, Plus, Pencil, Trash2, Star, Check, Folder, X } from 'lucide-react'

export function SettingsPage() {
  const queryClient = useQueryClient()
  const [form, setForm] = useState<Partial<AppSettings>>({})
  const [showAbout, setShowAbout] = useState(false)
  const importFileRef = useRef<HTMLInputElement>(null)

  const { data: settings, isLoading, error } = useQuery({
    queryKey: ['settings'],
    queryFn: settingsApi.get,
  })

  useEffect(() => {
    if (settings) setForm({ ...settings })
  }, [settings])

  const saveMut = useMutation({
    mutationFn: () => settingsApi.update(form),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['settings'] }),
  })

  const resetMut = useMutation({
    mutationFn: settingsApi.resetToDefaults,
    onSuccess: (data) => { setForm({ ...data }); queryClient.invalidateQueries({ queryKey: ['settings'] }) },
  })

  const handleExport = () => settingsApi.exportDb()

  const handleImport = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (!file) return
    settingsApi.importDb(file).then(() => {
      queryClient.invalidateQueries({ queryKey: ['settings'] })
      e.target.value = ''
    })
  }

  const handleReset = () => {
    if (confirm('Reset all settings to defaults? This cannot be undone.')) resetMut.mutate()
  }

  if (isLoading) return <div className="flex items-center justify-center h-full"><Loader2 className="animate-spin" size={32} /></div>
  if (error) return <div className="p-6 text-red-600">Failed to load settings: {(error as Error).message}</div>

  return (
    <div className="p-6 space-y-6 max-w-3xl">
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-bold text-gray-800">Settings</h2>
        <button onClick={() => setShowAbout(true)} className="flex items-center gap-1 px-3 py-1.5 bg-gray-200 text-gray-700 rounded text-sm hover:bg-gray-300">
          <Info size={14} /> About / {import.meta.env.VITE_APP_VERSION ?? 'dev'}
        </button>
      </div>

      {}
      <div className="bg-white rounded-lg shadow p-4 space-y-3">
        <h3 className="font-semibold text-gray-700">Application</h3>
        <div className="grid grid-cols-2 gap-3 text-sm">
          <div>
            <label className="block text-gray-500 mb-1">Port</label>
            <input type="number" value={form.port ?? 3131} onChange={e => setForm(f => ({ ...f, port: Number(e.target.value) }))}
              className="w-full border rounded px-3 py-1.5" />
          </div>
          <div>
            <label className="block text-gray-500 mb-1">Data Directory</label>
            <input value={form.dataDir ?? ''} onChange={e => setForm(f => ({ ...f, dataDir: e.target.value }))}
              className="w-full border rounded px-3 py-1.5" />
          </div>
        </div>
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" checked={form.openBrowserOnStart ?? true} onChange={e => setForm(f => ({ ...f, openBrowserOnStart: e.target.checked }))} />
          <span className="text-gray-600">Open browser on start</span>
        </label>
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" checked={form.relaunchOnStartup ?? false}
            onChange={e => setForm(f => ({ ...f, relaunchOnStartup: e.target.checked }))} />
          <span className="text-gray-600">Relaunch running instances on startup</span>
        </label>
      </div>

      {}
      <div className="bg-white rounded-lg shadow p-4 space-y-3">
        <h3 className="font-semibold text-gray-700">Monitoring</h3>
        <div className="text-sm">
          <label className="block text-gray-500 mb-1">Health Poll Interval (seconds)</label>
          <div className="flex items-center gap-3">
            <input type="range" min={1} max={60} value={form.healthPollIntervalSeconds ?? 10}
              onChange={e => setForm(f => ({ ...f, healthPollIntervalSeconds: Number(e.target.value) }))}
              className="flex-1" />
            <input type="number" min={1} max={60} value={form.healthPollIntervalSeconds ?? 10}
              onChange={e => setForm(f => ({ ...f, healthPollIntervalSeconds: Number(e.target.value) }))}
              className="w-16 border rounded px-2 py-1 text-center" />
          </div>
        </div>
      </div>

      {}
      <div className="bg-white rounded-lg shadow p-4 space-y-3">
        <h3 className="font-semibold text-gray-700">Data & Storage</h3>
        <p className="text-sm text-gray-500">Database: {form.dataDir ?? '~/.llamactrl'}/llamactrl.db</p>
        <div className="flex gap-2 flex-wrap">
          <button onClick={handleExport} className="flex items-center gap-1 px-3 py-1.5 bg-gray-200 text-gray-700 rounded text-sm hover:bg-gray-300">
            <Download size={14} /> Export All Data
          </button>
          <button onClick={() => importFileRef.current?.click()} className="flex items-center gap-1 px-3 py-1.5 bg-gray-200 text-gray-700 rounded text-sm hover:bg-gray-300">
            <Upload size={14} /> Import Data
          </button>
          <button onClick={() => settingsApi.openDataDir()} className="flex items-center gap-1 px-3 py-1.5 bg-gray-200 text-gray-700 rounded text-sm hover:bg-gray-300">
            <Folder size={14} /> Open Directory
          </button>
          <button onClick={handleReset} disabled={resetMut.isPending} className="flex items-center gap-1 px-3 py-1.5 bg-red-100 text-red-700 rounded text-sm hover:bg-red-200 disabled:opacity-50">
            <RotateCcw size={14} /> {resetMut.isPending ? 'Resetting...' : 'Reset to Defaults'}
          </button>
        </div>
        <input ref={importFileRef} type="file" accept=".db" className="hidden" onChange={handleImport} />
      </div>

      {}
      <BinariesSection />

      {}
      <ModelDirectoriesSection />

      {}
      <button onClick={() => saveMut.mutate()} disabled={saveMut.isPending}
        className="flex items-center gap-1 px-4 py-2 bg-[#4a9eed] text-white rounded text-sm hover:bg-[#3a8edd] disabled:opacity-50">
        <Save size={14} /> {saveMut.isPending ? 'Saving...' : 'Save Settings'}
      </button>

      {}
      {showAbout && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50" onClick={() => setShowAbout(false)}>
          <div className="bg-white rounded-lg shadow-xl p-6 w-80 space-y-3" onClick={e => e.stopPropagation()}>
            <h3 className="text-xl font-bold">LlamaCtrl</h3>
            <p className="text-gray-600">Version {import.meta.env.VITE_APP_VERSION ?? 'dev'}</p>
            <p className="text-sm text-gray-500">A lightweight GUI manager for llama.cpp server instances. Built with .NET and React.</p>
            <button onClick={() => setShowAbout(false)} className="px-4 py-2 bg-gray-200 text-gray-700 rounded text-sm hover:bg-gray-300">Close</button>
          </div>
        </div>
      )}
    </div>
  )
}

function BinariesSection() {
  const queryClient = useQueryClient()
  const [editing, setEditing] = useState<Partial<LlamaServerBinary> | null>(null)

  const { data: binaries, isLoading } = useQuery({
    queryKey: ['binaries'],
    queryFn: binariesApi.getAll,
  })

  const createMut = useMutation({
    mutationFn: (dto: { name: string; path: string; isDefault?: boolean }) => binariesApi.create(dto),
    onSuccess: () => { queryClient.invalidateQueries({ queryKey: ['binaries'] }); setEditing(null) },
  })

  const updateMut = useMutation({
    mutationFn: ({ id, dto }: { id: number; dto: { name?: string; path?: string; isDefault?: boolean } }) => binariesApi.update(id, dto),
    onSuccess: () => { queryClient.invalidateQueries({ queryKey: ['binaries'] }); setEditing(null) },
  })

  const deleteMut = useMutation({
    mutationFn: (id: number) => binariesApi.delete(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['binaries'] }),
  })

  const handleSave = () => {
    if (!editing?.name || !editing?.path) return
    if (editing.id) {
      updateMut.mutate({ id: editing.id, dto: { name: editing.name, path: editing.path } })
    } else {
      createMut.mutate({ name: editing.name, path: editing.path })
    }
  }

  const handleSetDefault = (id: number) => {
    updateMut.mutate({ id, dto: { isDefault: true } })
  }

  return (
    <div className="bg-white rounded-lg shadow p-4 space-y-3">
      <div className="flex items-center justify-between">
        <h3 className="font-semibold text-gray-700 flex items-center gap-2">
          <Folder size={16} /> llama-server Binaries
        </h3>
        <button
          onClick={() => setEditing({ name: '', path: '' })}
          className="flex items-center gap-1 px-2 py-1 bg-[#4a9eed] text-white rounded text-xs hover:bg-[#3a8edd]"
        >
          <Plus size={12} /> Add
        </button>
      </div>

      {isLoading && <Loader2 className="animate-spin" size={20} />}

      {binaries && binaries.length > 0 && (
        <table className="w-full text-sm">
          <thead className="bg-gray-50 border-b">
            <tr>
              <th className="text-left px-3 py-2 font-medium text-gray-600">Name</th>
              <th className="text-left px-3 py-2 font-medium text-gray-600">Path</th>
              <th className="text-left px-3 py-2 font-medium text-gray-600">Default</th>
              <th className="text-left px-3 py-2 font-medium text-gray-600">Actions</th>
            </tr>
          </thead>
          <tbody>
            {binaries.map(b => (
              <tr key={b.id} className="border-b hover:bg-gray-50">
                <td className="px-3 py-2">{b.name}</td>
                <td className="px-3 py-2 font-mono text-xs text-gray-600 truncate max-w-[200px]" title={b.path}>{b.path}</td>
                <td className="px-3 py-2">
                  {b.isDefault ? (
                    <span className="inline-flex items-center gap-1 text-yellow-600">
                      <Star size={14} fill="currentColor" /> Default
                    </span>
                  ) : (
                    <button
                      onClick={() => handleSetDefault(b.id)}
                      className="text-xs text-gray-500 hover:text-yellow-600"
                    >
                      Set as Default
                    </button>
                  )}
                </td>
                <td className="px-3 py-2">
                  <div className="flex items-center gap-1">
                    <button onClick={() => setEditing(b)} className="p-1 text-blue-600 hover:bg-blue-50 rounded" title="Edit">
                      <Pencil size={13} />
                    </button>
                    <button
                      onClick={() => { if (confirm('Delete this binary?')) deleteMut.mutate(b.id) }}
                      disabled={binaries.length <= 1}
                      className="p-1 text-red-600 hover:bg-red-50 rounded disabled:opacity-30 disabled:cursor-not-allowed"
                      title={binaries.length <= 1 ? 'Cannot delete the only binary' : 'Delete'}
                    >
                      <Trash2 size={13} />
                    </button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {binaries && binaries.length === 0 && !editing && (
        <p className="text-sm text-gray-400">No binaries configured. Add one to get started.</p>
      )}

      {editing && (
        <div className="border rounded p-3 space-y-2 bg-gray-50">
          <div className="grid grid-cols-2 gap-2 text-sm">
            <div>
              <label className="block text-gray-500 mb-1">Name</label>
              <input
                value={editing.name ?? ''}
                onChange={e => setEditing(p => ({ ...p, name: e.target.value }))}
                className="w-full border rounded px-3 py-1.5"
                placeholder="e.g. Default, CUDA build"
              />
            </div>
            <div>
              <label className="block text-gray-500 mb-1">Path</label>
              <input
                value={editing.path ?? ''}
                onChange={e => setEditing(p => ({ ...p, path: e.target.value }))}
                className="w-full border rounded px-3 py-1.5"
                placeholder="/usr/local/bin/llama-server"
              />
            </div>
          </div>
          <div className="flex gap-2">
            <button
              onClick={handleSave}
              disabled={!editing.name || !editing.path || createMut.isPending || updateMut.isPending}
              className="flex items-center gap-1 px-3 py-1.5 bg-[#4a9eed] text-white rounded text-xs hover:bg-[#3a8edd] disabled:opacity-50"
            >
              <Check size={12} /> {editing.id ? 'Update' : 'Add'}
            </button>
            <button onClick={() => setEditing(null)} className="px-3 py-1.5 bg-gray-200 text-gray-700 rounded text-xs hover:bg-gray-300">
              <X size={12} />
            </button>
          </div>
        </div>
      )}
    </div>
  )
}

function ModelDirectoriesSection() {
  const queryClient = useQueryClient()
  const [editing, setEditing] = useState<Partial<ModelDirectory> | null>(null)

  const { data: dirs, isLoading } = useQuery({
    queryKey: ['model-directories'],
    queryFn: modelDirectoriesApi.getAll,
  })

  const createMut = useMutation({
    mutationFn: (dto: { name: string; path: string }) => modelDirectoriesApi.create(dto),
    onSuccess: () => { queryClient.invalidateQueries({ queryKey: ['model-directories'] }); setEditing(null) },
  })

  const updateMut = useMutation({
    mutationFn: ({ id, dto }: { id: number; dto: { name?: string; path?: string } }) => modelDirectoriesApi.update(id, dto),
    onSuccess: () => { queryClient.invalidateQueries({ queryKey: ['model-directories'] }); setEditing(null) },
  })

  const deleteMut = useMutation({
    mutationFn: (id: number) => modelDirectoriesApi.delete(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['model-directories'] }),
  })

  const handleSave = () => {
    if (!editing?.name || !editing?.path) return
    if (editing.id) {
      updateMut.mutate({ id: editing.id, dto: { name: editing.name, path: editing.path } })
    } else {
      createMut.mutate({ name: editing.name, path: editing.path })
    }
  }

  return (
    <div className="bg-white rounded-lg shadow p-4 space-y-3">
      <div className="flex items-center justify-between">
        <h3 className="font-semibold text-gray-700 flex items-center gap-2">
          <Folder size={16} /> Model Directories
        </h3>
        <button
          onClick={() => setEditing({ name: '', path: '' })}
          className="flex items-center gap-1 px-2 py-1 bg-[#4a9eed] text-white rounded text-xs hover:bg-[#3a8edd]"
        >
          <Plus size={12} /> Add
        </button>
      </div>

      {isLoading && <Loader2 className="animate-spin" size={20} />}

      {dirs && dirs.length > 0 && (
        <table className="w-full text-sm">
          <thead className="bg-gray-50 border-b">
            <tr>
              <th className="text-left px-3 py-2 font-medium text-gray-600">Name</th>
              <th className="text-left px-3 py-2 font-medium text-gray-600">Path</th>
              <th className="text-left px-3 py-2 font-medium text-gray-600">Actions</th>
            </tr>
          </thead>
          <tbody>
            {dirs.map(d => (
              <tr key={d.id} className="border-b hover:bg-gray-50">
                <td className="px-3 py-2">{d.name}</td>
                <td className="px-3 py-2 font-mono text-xs text-gray-600 truncate max-w-[250px]" title={d.path}>{d.path}</td>
                <td className="px-3 py-2">
                  <div className="flex items-center gap-1">
                    <button onClick={() => setEditing(d)} className="p-1 text-blue-600 hover:bg-blue-50 rounded" title="Edit">
                      <Pencil size={13} />
                    </button>
                    <button
                      onClick={() => { if (confirm('Delete this directory?')) deleteMut.mutate(d.id) }}
                      disabled={dirs.length <= 1}
                      className="p-1 text-red-600 hover:bg-red-50 rounded disabled:opacity-30 disabled:cursor-not-allowed"
                      title={dirs.length <= 1 ? 'Cannot delete the only directory' : 'Delete'}
                    >
                      <Trash2 size={13} />
                    </button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {dirs && dirs.length === 0 && !editing && (
        <p className="text-sm text-gray-400">No model directories configured. Add one to get started.</p>
      )}

      {editing && (
        <div className="border rounded p-3 space-y-2 bg-gray-50">
          <div className="grid grid-cols-2 gap-2 text-sm">
            <div>
              <label className="block text-gray-500 mb-1">Name</label>
              <input
                value={editing.name ?? ''}
                onChange={e => setEditing(p => ({ ...p, name: e.target.value }))}
                className="w-full border rounded px-3 py-1.5"
                placeholder="e.g. Main, External SSD"
              />
            </div>
            <div>
              <label className="block text-gray-500 mb-1">Path</label>
              <input
                value={editing.path ?? ''}
                onChange={e => setEditing(p => ({ ...p, path: e.target.value }))}
                className="w-full border rounded px-3 py-1.5"
                placeholder="/home/user/models"
              />
            </div>
          </div>
          <div className="flex gap-2">
            <button
              onClick={handleSave}
              disabled={!editing.name || !editing.path || createMut.isPending || updateMut.isPending}
              className="flex items-center gap-1 px-3 py-1.5 bg-[#4a9eed] text-white rounded text-xs hover:bg-[#3a8edd] disabled:opacity-50"
            >
              <Check size={12} /> {editing.id ? 'Update' : 'Add'}
            </button>
            <button onClick={() => setEditing(null)} className="px-3 py-1.5 bg-gray-200 text-gray-700 rounded text-xs hover:bg-gray-300">
              <X size={12} />
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
