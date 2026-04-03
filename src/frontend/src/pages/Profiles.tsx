import { useState, useEffect, useRef } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { profilesApi } from '../api/profiles'
import { modelsApi } from '../api/models'
import { binariesApi } from '../api/binaries'
import type { Profile, ProfileParameter } from '../api/types'
import { LLAMA_PARAM_CATALOG } from '../constants/llamaParamCatalog'
import { Plus, Play, Pencil, Copy, Loader2, Save, Upload, Download, X } from 'lucide-react'

const fmtSize = (bytes: number) => {
  if (bytes >= 1e9) return `${(bytes / 1e9).toFixed(1)} GB`
  if (bytes >= 1e6) return `${(bytes / 1e6).toFixed(1)} MB`
  return `${(bytes / 1e3).toFixed(0)} KB`
}

const defaultProfile: Partial<Profile> = {
  name: '',
  modelPath: '',
  selectedBinaryId: null,
  parameters: {},
  customArgs: [],
}

function quoteIfNeeded(value: string): string {
  if (value.includes(' ') || value.includes('"'))
    return `"${value.replace(/"/g, '\\"')}"`
  return value
}

function buildCliPreview(profile: Partial<Profile>): string {
  const parts = ['llama-server', '-m', `"${profile.modelPath || '<model>'}"`, '--port', '<port>']
  for (const [flag, value] of Object.entries(profile.parameters ?? {})) {
    parts.push(flag)
    if (value) parts.push(quoteIfNeeded(value))
  }
  for (const { flag, value } of (profile.customArgs ?? [])) {
    if (flag) { parts.push(flag); if (value) parts.push(quoteIfNeeded(value)) }
  }
  return parts.join(' ')
}

function tokenizeCli(s: string): string[] {
  const tokens: string[] = []
  let cur = ''
  let inQ = false
  for (let i = 0; i < s.length; i++) {
    const c = s[i]
    if (inQ) {
      if (c === '\\' && s[i+1] === '"') { cur += '"'; i++ }
      else if (c === '"') inQ = false
      else cur += c
    } else if (c === '"') { inQ = true }
    else if (c === ' ') { if (cur) { tokens.push(cur); cur = '' } }
    else cur += c
  }
  if (cur) tokens.push(cur)
  return tokens
}

function parseCliString(cli: string): Pick<Profile, 'parameters' | 'customArgs'> {
  const tokens = tokenizeCli(cli)
  const knownFlags = new Set(LLAMA_PARAM_CATALOG.map(e => e.flag))
  const parameters: Record<string, string> = {}
  const customArgs: ProfileParameter[] = []

  let i = 0
  if (tokens[i] === 'llama-server') i++
  if (tokens[i] === '-m') { i += 2 }
  if (tokens[i] === '--port') { i += 2 }

  while (i < tokens.length) {
    const flag = tokens[i]
    if (!flag.startsWith('-')) { i++; continue }
    let value = ''
    const isFlag = (t: string) => t.startsWith('-') && !/^-[\d.]/.test(t)
    if (i + 1 < tokens.length && !isFlag(tokens[i + 1])) {
      value = tokens[i + 1]; i += 2
    } else { i++ }
    if (knownFlags.has(flag)) parameters[flag] = value
    else customArgs.push({ flag, value })
  }
  return { parameters, customArgs }
}

function getCatalogEntry(flag: string) {
  return LLAMA_PARAM_CATALOG.find(e => e.flag === flag)
}

export function Profiles() {
  const queryClient = useQueryClient()
  const [editingProfile, setEditingProfile] = useState<Partial<Profile> | null>(null)
  const [manualPath, setManualPath] = useState(false)
  const [showParamPicker, setShowParamPicker] = useState(false)
  const [paramSearch, setParamSearch] = useState('')
  const pickerRef = useRef<HTMLDivElement>(null)
  const [cliText, setCliText] = useState('')
  const cliEditingRef = useRef<boolean>(false)

  const { data: profiles, isLoading, error } = useQuery({
    queryKey: ['profiles'],
    queryFn: profilesApi.getAll,
  })

  const localModels = useQuery({
    queryKey: ['models', 'local'],
    queryFn: modelsApi.getLocal,
    staleTime: 30_000,
  })

  const { data: binaries } = useQuery({
    queryKey: ['binaries'],
    queryFn: binariesApi.getAll,
  })

  useEffect(() => {
    if (!cliEditingRef.current && editingProfile) {
      setCliText(buildCliPreview(editingProfile))
    }
  }, [editingProfile])

  useEffect(() => {
    if (!editingProfile) {
      setManualPath(false)
      return
    }
    const isKnown = localModels.data?.some(m => m.fullPath === editingProfile.modelPath)
    setManualPath(!isKnown && !!editingProfile.modelPath)
  }, [editingProfile?.id])

  useEffect(() => {
    if (!showParamPicker) return
    const handler = (e: MouseEvent) => {
      if (pickerRef.current && !pickerRef.current.contains(e.target as Node)) {
        setShowParamPicker(false)
        setParamSearch('')
      }
    }
    document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
  }, [showParamPicker])

  useEffect(() => {
    if (!showParamPicker) return
    const handler = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        setShowParamPicker(false)
        setParamSearch('')
      }
    }
    document.addEventListener('keydown', handler)
    return () => document.removeEventListener('keydown', handler)
  }, [showParamPicker])

  const [saveError, setSaveError] = useState<string | null>(null)

  const saveMut = useMutation({
    mutationFn: async (data: Partial<Profile>) => {
      if (data.id) return profilesApi.update(data.id, data)
      return profilesApi.create(data)
    },
    onSuccess: (saved) => {
      setSaveError(null)
      queryClient.invalidateQueries({ queryKey: ['profiles'] })
      setEditingProfile(saved)
    },
    onError: (e: Error) => setSaveError(e.message),
  })

  const deleteMut = useMutation({
    mutationFn: profilesApi.delete,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['profiles'] })
      setEditingProfile(null)
    },
  })

  const cloneMut = useMutation({
    mutationFn: (id: number) => profilesApi.clone(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['profiles'] }),
  })

  const launchMut = useMutation({
    mutationFn: profilesApi.launch,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['instances'] }),
  })

  const handleExport = () => {
    if (!profiles) return
    const blob = new Blob([JSON.stringify(profiles, null, 2)], { type: 'application/json' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url; a.download = 'profiles.json'; a.click()
    URL.revokeObjectURL(url)
  }

  const handleImport = () => {
    const input = document.createElement('input')
    input.type = 'file'; input.accept = '.json'
    input.onchange = async (e) => {
      const file = (e.target as HTMLInputElement).files?.[0]
      if (!file) return
      const text = await file.text()
      const data = JSON.parse(text) as Partial<Profile>[]
      for (const p of data) await profilesApi.create(p)
      queryClient.invalidateQueries({ queryKey: ['profiles'] })
    }
    input.click()
  }

  const addParameter = (flag: string) => {
    if (!editingProfile) return
    const entry = getCatalogEntry(flag)
    const value = entry?.type === 'flag' ? '' : (entry?.defaultValue ?? '')
    setEditingProfile(p => ({
      ...p,
      parameters: { ...(p?.parameters ?? {}), [flag]: value },
    }))
    setShowParamPicker(false)
    setParamSearch('')
  }

  const removeParameter = (flag: string) => {
    if (!editingProfile) return
    setEditingProfile(p => {
      const next = { ...(p?.parameters ?? {}) }
      delete next[flag]
      return { ...p, parameters: next }
    })
  }

  const updateParameter = (flag: string, value: string) => {
    if (!editingProfile) return
    setEditingProfile(p => ({
      ...p,
      parameters: { ...(p?.parameters ?? {}), [flag]: value },
    }))
  }

  const addCustomArg = () => {
    if (!editingProfile) return
    setEditingProfile(p => ({
      ...p,
      customArgs: [...(p?.customArgs ?? []), { flag: '', value: '' }],
    }))
  }

  const removeCustomArg = (index: number) => {
    if (!editingProfile) return
    setEditingProfile(p => ({
      ...p,
      customArgs: (p?.customArgs ?? []).filter((_, i) => i !== index),
    }))
  }

  const updateCustomArg = (index: number, field: keyof ProfileParameter, value: string) => {
    if (!editingProfile) return
    setEditingProfile(p => ({
      ...p,
      customArgs: (p?.customArgs ?? []).map((a, i) => i === index ? { ...a, [field]: value } : a),
    }))
  }

  const alreadyAddedFlags = new Set(Object.keys(editingProfile?.parameters ?? {}))
  const filteredCatalog = LLAMA_PARAM_CATALOG.filter(entry =>
    !alreadyAddedFlags.has(entry.flag) &&
    (paramSearch === '' ||
      entry.label.toLowerCase().includes(paramSearch.toLowerCase()) ||
      entry.flag.toLowerCase().includes(paramSearch.toLowerCase()) ||
      entry.description.toLowerCase().includes(paramSearch.toLowerCase()))
  )

  if (isLoading) return <div className="flex items-center justify-center h-full"><Loader2 className="animate-spin" size={32} /></div>
  if (error) return <div className="p-6 text-red-600">Failed to load profiles: {(error as Error).message}</div>

  return (
    <div className="p-6 flex gap-6 h-full">
      {}
      <div className="flex-1 space-y-4 overflow-auto">
        <div className="flex items-center justify-between">
          <h2 className="text-2xl font-bold text-gray-800">Profiles</h2>
          <div className="flex gap-2">
            <button onClick={() => setEditingProfile({ ...defaultProfile })} className="flex items-center gap-1 px-3 py-1.5 bg-[#4a9eed] text-white rounded text-sm hover:bg-[#3a8edd]">
              <Plus size={14} /> New Profile
            </button>
            <button onClick={handleImport} className="flex items-center gap-1 px-3 py-1.5 bg-gray-200 text-gray-700 rounded text-sm hover:bg-gray-300">
              <Upload size={14} /> Import JSON
            </button>
            <button onClick={handleExport} className="flex items-center gap-1 px-3 py-1.5 bg-gray-200 text-gray-700 rounded text-sm hover:bg-gray-300">
              <Download size={14} /> Export JSON
            </button>
          </div>
        </div>
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-3">
          {profiles?.map(p => {
            const paramEntries = Object.entries(p.parameters)
            return (
              <div key={p.id}
                className={`bg-white rounded-lg shadow p-4 border-2 transition-colors cursor-pointer ${editingProfile?.id === p.id ? 'border-[#4a9eed]' : 'border-transparent hover:border-gray-200'}`}
                onClick={() => setEditingProfile({ ...p })}>
                <div className="flex items-center justify-between mb-2">
                  <h3 className="font-semibold">{p.name}</h3>
                  <div className="flex gap-1" onClick={e => e.stopPropagation()}>
                    <button onClick={() => launchMut.mutate(p.id)} title="Launch" className="p-1 text-green-600 hover:bg-green-50 rounded"><Play size={14} /></button>
                    <button onClick={() => setEditingProfile({ ...p })} title="Edit" className="p-1 text-blue-600 hover:bg-blue-50 rounded"><Pencil size={14} /></button>
                    <button onClick={() => cloneMut.mutate(p.id)} title="Clone" className="p-1 text-gray-600 hover:bg-gray-100 rounded"><Copy size={14} /></button>
                  </div>
                </div>
                <p className="text-xs text-gray-500 truncate mb-2">{p.modelPath.split('/').pop()}</p>
                {paramEntries.length === 0 ? (
                  <p className="text-xs text-gray-400">No parameters</p>
                ) : (
                  <div className="flex flex-wrap gap-1">
                    {paramEntries.slice(0, 4).map(([flag, value]) => {
                      const entry = getCatalogEntry(flag)
                      const label = entry?.label ?? flag
                      return (
                        <span key={flag} className="inline-block bg-gray-100 text-gray-600 text-xs px-2 py-0.5 rounded">
                          {entry?.type === 'flag' ? label : `${label}: ${value}`}
                        </span>
                      )
                    })}
                    {paramEntries.length > 4 && (
                      <span className="text-xs text-gray-400">+{paramEntries.length - 4} more</span>
                    )}
                  </div>
                )}
              </div>
            )
          })}
        </div>
      </div>

      {}
      {editingProfile && (
        <div className="w-[28rem] bg-white rounded-lg shadow p-4 space-y-4 overflow-auto flex-shrink-0">
          <div className="flex items-center justify-between">
            <h3 className="font-semibold">{editingProfile.id ? 'Edit Profile' : 'New Profile'}</h3>
            {editingProfile.id && (
              <button onClick={() => deleteMut.mutate(editingProfile.id!)} className="text-red-600 text-sm hover:underline">Delete</button>
            )}
          </div>

          <div className="space-y-3 text-sm">
            {}
            <div>
              <label className="block text-gray-600 mb-1">Name</label>
              <input value={editingProfile.name ?? ''} onChange={e => { setSaveError(null); setEditingProfile(p => ({ ...p, name: e.target.value })) }}
                className="w-full border rounded px-3 py-1.5 focus:outline-none focus:ring-2 focus:ring-[#4a9eed]" />
            </div>

            {}
            <div>
              <label className="block text-gray-600 mb-1">Model Path</label>
              {(manualPath || (localModels.data && localModels.data.length === 0)) ? (
                <>
                  {localModels.data && localModels.data.length === 0 && (
                    <p className="text-xs text-gray-500 mb-1">No models found in models directory</p>
                  )}
                  <input value={editingProfile.modelPath ?? ''} onChange={e => setEditingProfile(p => ({ ...p, modelPath: e.target.value }))}
                    className="w-full border rounded px-3 py-1.5 focus:outline-none focus:ring-2 focus:ring-[#4a9eed]" placeholder="/path/to/model.gguf" />
                  {!(localModels.data && localModels.data.length === 0) && (
                    <button type="button" onClick={() => {
                      const isKnown = localModels.data?.some(m => m.fullPath === editingProfile.modelPath)
                      if (!isKnown) setEditingProfile(p => ({ ...p, modelPath: '' }))
                      setManualPath(false)
                    }} className="text-xs text-blue-600 hover:underline mt-1">
                      ← Choose from models
                    </button>
                  )}
                </>
              ) : (
                <>
                  <select
                    value={editingProfile.modelPath ?? ''}
                    onChange={e => setEditingProfile(p => ({ ...p, modelPath: e.target.value }))}
                    disabled={localModels.isLoading}
                    className="w-full border rounded px-3 py-1.5 focus:outline-none focus:ring-2 focus:ring-[#4a9eed]"
                  >
                    {localModels.isLoading ? (
                      <option value="">Loading models...</option>
                    ) : (
                      <>
                        <option value="">Select a model...</option>
                        {localModels.data?.map(model => (
                          <option key={model.fullPath} value={model.fullPath}>
                            {model.filename} ({fmtSize(model.sizeBytes)})
                          </option>
                        ))}
                      </>
                    )}
                  </select>
                  <button type="button" onClick={() => setManualPath(true)} className="text-xs text-blue-600 hover:underline mt-1">
                    Enter path manually
                  </button>
                </>
              )}
            </div>

            {}
            {binaries && binaries.length > 0 && (
              <div>
                <label className="block text-gray-600 mb-1">llama-server Binary</label>
                <select
                  value={editingProfile.selectedBinaryId ?? (binaries.find(b => b.isDefault)?.id ?? binaries[0].id)}
                  onChange={e => setEditingProfile(p => ({
                    ...p,
                    selectedBinaryId: Number(e.target.value),
                  }))}
                  className="w-full border rounded px-3 py-1.5 focus:outline-none focus:ring-2 focus:ring-[#4a9eed]"
                >
                  {binaries.map(b => (
                    <option key={b.id} value={b.id}>
                      {b.name}{b.isDefault ? ' (default)' : ''} — {b.path}
                    </option>
                  ))}
                </select>
              </div>
            )}

            {}
            <div>
              <label className="block text-gray-600 mb-2 font-medium">Parameters</label>
              {Object.keys(editingProfile.parameters ?? {}).length > 0 && (
                <div className="space-y-2 mb-2">
                  {Object.entries(editingProfile.parameters ?? {}).map(([flag, value]) => {
                    const entry = getCatalogEntry(flag)
                    const label = entry?.label ?? flag
                    return (
                      <div key={flag} className="flex items-center gap-2 bg-gray-50 border rounded px-2 py-1.5">
                        <span className="text-gray-700 text-xs font-medium w-28 flex-shrink-0">{label}</span>
                        <div className="flex-1 min-w-0">
                          {entry?.type === 'flag' ? (
                            <span className="text-xs text-gray-400 italic">flag (no value)</span>
                          ) : entry?.type === 'select' ? (
                            <select
                              value={value}
                              onChange={e => updateParameter(flag, e.target.value)}
                              className="w-full border rounded px-2 py-0.5 text-xs focus:outline-none focus:ring-1 focus:ring-[#4a9eed]"
                            >
                              {entry.options?.map(opt => (
                                <option key={opt} value={opt}>{opt}</option>
                              ))}
                            </select>
                          ) : entry?.type === 'text' && flag === '-sp' ? (
                            <textarea
                              rows={3}
                              value={value}
                              onChange={e => updateParameter(flag, e.target.value)}
                              className="w-full border rounded px-2 py-0.5 text-xs resize-y focus:outline-none focus:ring-1 focus:ring-[#4a9eed]"
                            />
                          ) : (
                            <input
                              type={entry?.type === 'number' ? 'number' : 'text'}
                              step={entry?.step}
                              value={value}
                              onChange={e => updateParameter(flag, e.target.value)}
                              className="w-full border rounded px-2 py-0.5 text-xs focus:outline-none focus:ring-1 focus:ring-[#4a9eed]"
                            />
                          )}
                        </div>
                        <button onClick={() => removeParameter(flag)} className="text-gray-400 hover:text-red-500 flex-shrink-0">
                          <X size={13} />
                        </button>
                      </div>
                    )
                  })}
                </div>
              )}

              {}
              <div ref={pickerRef} className="relative">
                <button
                  type="button"
                  onClick={() => { setShowParamPicker(v => !v); setParamSearch('') }}
                  className="flex items-center gap-1 text-xs text-blue-600 hover:underline"
                >
                  <Plus size={12} /> Add parameter
                </button>
                {showParamPicker && (
                  <div className="absolute left-0 top-6 z-50 w-80 bg-white border border-gray-200 rounded-lg shadow-lg overflow-hidden">
                    <div className="p-2 border-b">
                      <input
                        autoFocus
                        type="text"
                        placeholder="Search parameters..."
                        value={paramSearch}
                        onChange={e => setParamSearch(e.target.value)}
                        className="w-full border rounded px-2 py-1 text-xs focus:outline-none focus:ring-1 focus:ring-[#4a9eed]"
                      />
                    </div>
                    <div className="max-h-56 overflow-y-auto">
                      {filteredCatalog.length === 0 ? (
                        <p className="text-xs text-gray-400 px-3 py-4 text-center">No matching parameters</p>
                      ) : (
                        filteredCatalog.map(entry => (
                          <button
                            key={entry.flag}
                            type="button"
                            onClick={() => addParameter(entry.flag)}
                            className="w-full text-left px-3 py-2 hover:bg-blue-50 transition-colors"
                          >
                            <div className="flex items-baseline gap-2">
                              <span className="text-xs font-semibold text-gray-800">{entry.label}</span>
                              <span className="text-xs text-gray-400 font-mono">{entry.flag}</span>
                            </div>
                            <p className="text-xs text-gray-400 mt-0.5">{entry.description}</p>
                          </button>
                        ))
                      )}
                    </div>
                  </div>
                )}
              </div>
            </div>

            {}
            <div>
              <label className="block text-gray-600 mb-2 font-medium">Custom Parameters</label>
              {(editingProfile.customArgs ?? []).length > 0 && (
                <div className="space-y-2 mb-2">
                  {(editingProfile.customArgs ?? []).map((arg, i) => (
                    <div key={i} className="flex items-center gap-2">
                      <input
                        type="text"
                        placeholder="--flag"
                        value={arg.flag}
                        onChange={e => updateCustomArg(i, 'flag', e.target.value)}
                        className="w-28 border rounded px-2 py-1 text-xs font-mono focus:outline-none focus:ring-1 focus:ring-[#4a9eed]"
                      />
                      <input
                        type="text"
                        placeholder="value (optional)"
                        value={arg.value}
                        onChange={e => updateCustomArg(i, 'value', e.target.value)}
                        className="flex-1 border rounded px-2 py-1 text-xs focus:outline-none focus:ring-1 focus:ring-[#4a9eed]"
                      />
                      <button onClick={() => removeCustomArg(i)} className="text-gray-400 hover:text-red-500">
                        <X size={13} />
                      </button>
                    </div>
                  ))}
                </div>
              )}
              <button
                type="button"
                onClick={addCustomArg}
                className="flex items-center gap-1 text-xs text-blue-600 hover:underline"
              >
                <Plus size={12} /> Add custom parameter
              </button>
            </div>

            {}
            <div>
              <label className="block text-gray-600 mb-1 font-medium">
                CLI Preview
                <span className="ml-2 text-xs font-normal text-gray-400">— editable</span>
              </label>
              <textarea
                className="w-full bg-gray-900 text-gray-100 text-xs rounded p-3 font-mono leading-5 resize-none focus:outline-none focus:ring-2 focus:ring-blue-500"
                rows={4}
                value={cliText}
                onChange={e => {
                  const text = e.target.value
                  setCliText(text)
                  cliEditingRef.current = true
                  const parsed = parseCliString(text)
                  setEditingProfile(p => p ? { ...p, ...parsed } : p)
                }}
                onBlur={() => {
                  cliEditingRef.current = false
                  if (editingProfile) setCliText(buildCliPreview(editingProfile))
                }}
                spellCheck={false}
              />
            </div>
          </div>

          {saveError && <p className="text-xs text-red-600">{saveError}</p>}

          <div className="flex gap-2 pt-2">
            <button
              onClick={() => saveMut.mutate(editingProfile)}
              disabled={saveMut.isPending}
              className="flex items-center gap-1 px-3 py-1.5 bg-[#4a9eed] text-white rounded text-sm hover:bg-[#3a8edd] disabled:opacity-50"
            >
              <Save size={14} /> {saveMut.isPending ? 'Saving...' : 'Save'}
            </button>
            <button
              onClick={async () => {
                const saved = await saveMut.mutateAsync(editingProfile)
                if (saved?.id) launchMut.mutate(saved.id)
              }}
              disabled={saveMut.isPending}
              className="flex items-center gap-1 px-3 py-1.5 bg-green-600 text-white rounded text-sm hover:bg-green-700 disabled:opacity-50"
            >
              <Play size={14} /> Save + Launch
            </button>
            <button onClick={() => setEditingProfile(null)} className="px-3 py-1.5 bg-gray-200 text-gray-700 rounded text-sm hover:bg-gray-300">Cancel</button>
          </div>
        </div>
      )}
    </div>
  )
}
