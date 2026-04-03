import { useState, useRef, useEffect } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { HardDrive, Search, Download, Copy, X, RefreshCw, ExternalLink, Loader2, CheckCircle2, Heart } from 'lucide-react'
import * as signalR from '@microsoft/signalr'
import { modelsApi } from '../api/models'
import { modelDirectoriesApi } from '../api/modelDirectories'
import type { DownloadProgress, HfSearchResult, HfModelInfo, LocalModel } from '../api/types'

const toGroupName = (id: string) => id.replace(/\//g, '_')

function fmtBytes(bytes?: number): string {
  if (bytes == null) return '—'
  if (bytes >= 1_073_741_824) return `${(bytes / 1_073_741_824).toFixed(1)} GB`
  if (bytes >= 1_048_576) return `${(bytes / 1_048_576).toFixed(1)} MB`
  if (bytes >= 1_024) return `${(bytes / 1_024).toFixed(1)} KB`
  return `${bytes} B`
}

function fmtDate(iso?: string): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleDateString()
}

function fmtDownloads(n: number): string {
  if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(1)}M`
  if (n >= 1_000) return `${(n / 1_000).toFixed(1)}k`
  return String(n)
}

const PHASE_LABELS: Record<string, string> = {
  starting: 'Starting…',
  downloading: 'Downloading',
  done: 'Complete',
  error: 'Error',
  cancelled: 'Cancelled',
}

interface DownloadRowProps {
  downloadId: string
  progress: DownloadProgress
  onCancel: (id: string) => void
}

function DownloadRow({ downloadId, progress, onCancel }: DownloadRowProps) {
  const phase = progress.phase ?? 'starting'
  const pct = progress.percentComplete ?? 0
  const isDone = phase === 'done'
  const isError = phase === 'error'
  const isCancelled = phase === 'cancelled'
  const isActive = !isDone && !isError && !isCancelled

  return (
    <div className="bg-white rounded-lg shadow p-4 space-y-2">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2 min-w-0">
          {isDone
            ? <CheckCircle2 size={16} className="text-green-500 flex-shrink-0" />
            : isError
              ? <X size={16} className="text-red-500 flex-shrink-0" />
              : <Loader2 size={16} className="animate-spin text-blue-500 flex-shrink-0" />
          }
          <span className="text-sm font-medium truncate">{progress.filename}</span>
        </div>
        <div className="flex items-center gap-3 flex-shrink-0 ml-3">
          <span className={`text-xs px-2 py-0.5 rounded font-medium ${
            isDone ? 'bg-green-100 text-green-700'
              : isError ? 'bg-red-100 text-red-700'
              : isCancelled ? 'bg-gray-100 text-gray-600'
              : 'bg-blue-100 text-blue-700'
          }`}>
            {PHASE_LABELS[phase] ?? phase}
          </span>
          {isActive && (
            <button
              onClick={() => onCancel(downloadId)}
              className="flex items-center gap-1 px-2 py-1 bg-red-100 text-red-700 rounded text-xs hover:bg-red-200 transition-colors"
            >
              <X size={12} /> Cancel
            </button>
          )}
        </div>
      </div>

      {isActive && (
        <>
          {phase === 'starting' ? (
            <div className="w-full bg-gray-200 rounded-full h-2 overflow-hidden">
              <div className="h-2 bg-blue-400 rounded-full animate-pulse w-1/3" />
            </div>
          ) : (
            <div className="w-full bg-gray-200 rounded-full h-2">
              <div
                className="bg-blue-500 h-2 rounded-full transition-all duration-300"
                style={{ width: `${Math.min(Math.max(progress.percentComplete, 0), 100)}%` }}
              />
            </div>
          )}
          <div className="flex justify-between text-xs text-gray-500 mt-1">
            <span>{fmtBytes(progress.bytesReceived)} {progress.totalBytes ? `/ ${fmtBytes(progress.totalBytes)}` : 'downloaded'}</span>
            {progress.totalBytes && progress.percentComplete > 0 && (
              <span>{Math.round(progress.percentComplete)}%</span>
            )}
          </div>
        </>
      )}

      {isError && progress.error && (
        <p className="text-xs text-red-600">{progress.error}</p>
      )}
    </div>
  )
}

export function Models() {
  const queryClient = useQueryClient()

  const [searchInput, setSearchInput] = useState('')
  const [searchQuery, setSearchQuery] = useState('')
  const [selectedModel, setSelectedModel] = useState<HfSearchResult | null>(null)
  const [activeDownloads, setActiveDownloads] = useState<Map<string, DownloadProgress>>(new Map())
  const connectionsRef = useRef<Map<string, signalR.HubConnection>>(new Map())
  const [copiedPath, setCopiedPath] = useState<string | null>(null)
  const [selectedDownloadDir, setSelectedDownloadDir] = useState<string>('')

  const detailPanelRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (selectedModel && detailPanelRef.current) {
      detailPanelRef.current.scrollIntoView({ behavior: 'smooth', block: 'start' })
    }
  }, [selectedModel])

  useEffect(() => {
    return () => {
      connectionsRef.current.forEach(conn => conn.stop())
    }
  }, [])

  useEffect(() => {
    modelsApi.getActiveDownloads().then((res: any) => {
      const active: DownloadProgress[] = res?.data ?? res ?? []
      if (!Array.isArray(active) || active.length === 0) return
      active.forEach(async (progress: DownloadProgress) => {
        const downloadId = progress.downloadId
        if (connectionsRef.current.has(downloadId)) return
        const connection = new signalR.HubConnectionBuilder()
          .withUrl('/hubs/downloads').withAutomaticReconnect().build()
        connection.on('DownloadProgress', (p: DownloadProgress) => {
          setActiveDownloads(prev => { const next = new Map(prev); next.set(downloadId, p); return next })
          if (p.isComplete) {
            setTimeout(() => {
              connection.invoke('LeaveDownload', toGroupName(downloadId)).catch(() => {}).finally(() => connection.stop())
              connectionsRef.current.delete(downloadId)
              queryClient.invalidateQueries({ queryKey: ['models', 'local'] })
              setActiveDownloads(prev => { const next = new Map(prev); next.delete(downloadId); return next })
            }, 1500)
          }
        })
        try {
          await connection.start()
          await connection.invoke('JoinDownload', toGroupName(downloadId))
          connectionsRef.current.set(downloadId, connection)
          setActiveDownloads(prev => new Map(prev).set(downloadId, progress))
        } catch {
          connection.stop()
        }
      })
    }).catch(() => {})
  }, []) // eslint-disable-line react-hooks/exhaustive-deps

  const { data: localModels, isLoading: localLoading, error: localError } = useQuery({
    queryKey: ['models', 'local'],
    queryFn: modelsApi.getLocal,
  })

  const { data: hfCliStatus } = useQuery({
    queryKey: ['models', 'hf-cli'],
    queryFn: modelsApi.checkHfCli,
  })

  const { data: searchResults, isLoading: searchLoading, error: searchError } = useQuery({
    queryKey: ['models', 'search', searchQuery],
    queryFn: () => modelsApi.search(searchQuery),
    enabled: searchQuery.length > 0,
  })

  const { data: modelDetail, isLoading: detailLoading, error: detailError } = useQuery({
    queryKey: ['models', 'hf', selectedModel?.modelId],
    queryFn: () => modelsApi.getHfModel(selectedModel!.modelId),
    enabled: !!selectedModel,
  })

  const { data: modelDirectories } = useQuery({
    queryKey: ['model-directories'],
    queryFn: modelDirectoriesApi.getAll,
  })

  useEffect(() => {
    if (modelDirectories && modelDirectories.length > 0 && !selectedDownloadDir) {
      setSelectedDownloadDir(modelDirectories[0].path)
    }
  }, [modelDirectories])

  function handleSearch(e: React.FormEvent) {
    e.preventDefault()
    setSearchQuery(searchInput.trim())
    setSelectedModel(null)
  }

  async function handleDownload(modelId: string, filename: string, sizeBytes?: number, extra?: { author?: string | null; description?: string | null; tags?: string[] | null; hfDownloads?: number | null; hfLikes?: number | null }, targetDirectory?: string) {
    const downloadId = `${modelId}/${filename}`

    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/downloads')
      .withAutomaticReconnect()
      .build()

    connection.on('DownloadProgress', (p: DownloadProgress) => {
      setActiveDownloads(prev => {
        const next = new Map(prev)
        next.set(downloadId, p)
        return next
      })
      if (p.isComplete) {
        setTimeout(() => {
          connection.invoke('LeaveDownload', toGroupName(downloadId)).catch(() => {})
            .finally(() => connection.stop())
          connectionsRef.current.delete(downloadId)
          queryClient.invalidateQueries({ queryKey: ['models', 'local'] })
          setActiveDownloads(prev => {
            const next = new Map(prev)
            next.delete(downloadId)
            return next
          })
        }, 1500)
      }
    })

    try {
      await connection.start()

      await connection.invoke('JoinDownload', toGroupName(downloadId))

      const pingRes = await fetch('/api/models/download/ping', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ downloadId }),
      })
      const pingJson = await pingRes.json()

      connectionsRef.current.set(downloadId, connection)

      setActiveDownloads(prev => new Map(prev).set(downloadId, {
        downloadId,
        filename,
        phase: 'starting',
        bytesReceived: 0,
        percentComplete: 0,
        isComplete: false,
      }))

      await modelsApi.startDownload(modelId, filename, sizeBytes, extra, targetDirectory)
    } catch (err) {
      setActiveDownloads(prev => new Map(prev).set(downloadId, {
        downloadId,
        filename,
        phase: 'error',
        bytesReceived: 0,
        percentComplete: 0,
        error: String(err),
        isComplete: true,
      }))
      connection.stop()
      connectionsRef.current.delete(downloadId)
    }
  }

  async function handleCancelDownload(downloadId: string) {
    await modelsApi.cancelDownload(downloadId).catch(console.error)
    const conn = connectionsRef.current.get(downloadId)
    if (conn) {
      conn.invoke('LeaveDownload', toGroupName(downloadId)).catch(() => {}).finally(() => conn.stop())
      connectionsRef.current.delete(downloadId)
    }
    setActiveDownloads(prev => {
      const next = new Map(prev)
      next.delete(downloadId)
      return next
    })
  }

  function handleCopyPath(path: string) {
    navigator.clipboard.writeText(path).then(() => {
      setCopiedPath(path)
      setTimeout(() => setCopiedPath(null), 2000)
    })
  }

  return (
    <div className="p-6 space-y-6">
      {}
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-bold text-gray-800 flex items-center gap-2">
          <HardDrive size={24} className="text-[#4a9eed]" />
          Models
        </h2>
        {hfCliStatus != null && (
          <span className={`text-xs px-3 py-1 rounded-full font-medium border ${
            hfCliStatus.available
              ? 'bg-green-50 text-green-700 border-green-300'
              : 'bg-yellow-50 text-yellow-700 border-yellow-300'
          }`}>
            {hfCliStatus.available ? 'Using huggingface-cli for downloads' : 'Using direct HTTP download'}
          </span>
        )}
      </div>

      {}
      <section className="space-y-3">
        <div className="flex items-center justify-between">
          <h3 className="text-lg font-semibold text-gray-700">Local Models</h3>
          <button
            onClick={() => queryClient.invalidateQueries({ queryKey: ['models', 'local'] })}
            className="flex items-center gap-1 px-3 py-1.5 bg-gray-200 text-gray-700 rounded text-sm hover:bg-gray-300 transition-colors"
          >
            <RefreshCw size={14} /> Refresh
          </button>
        </div>

        {localLoading && (
          <div className="flex items-center justify-center py-8">
            <Loader2 className="animate-spin text-[#4a9eed]" size={28} />
          </div>
        )}
        {localError && (
          <div className="p-4 bg-red-50 border border-red-200 rounded text-red-700 text-sm">
            Failed to load local models: {(localError as Error).message}
          </div>
        )}
        {!localLoading && !localError && (
          <div className="bg-white rounded-lg shadow overflow-hidden">
            <table className="w-full text-sm">
              <thead className="bg-gray-50 border-b">
                <tr>
                  <th className="text-left px-4 py-3 font-medium text-gray-600">Filename</th>
                  <th className="text-left px-4 py-3 font-medium text-gray-600">Directory</th>
                  <th className="text-left px-4 py-3 font-medium text-gray-600">Size</th>
                  <th className="text-left px-4 py-3 font-medium text-gray-600">Modified</th>
                  <th className="text-left px-4 py-3 font-medium text-gray-600">Actions</th>
                </tr>
              </thead>
              <tbody>
                {localModels?.map((m: LocalModel) => (
                  <tr key={m.fullPath} className="border-b hover:bg-blue-50 transition-colors">
                    <td className="px-4 py-3">
                      <div className="font-mono text-xs text-gray-800">{m.filename}</div>
                      {m.hfMeta && (
                        <div className="mt-1 flex items-center gap-2 flex-wrap">
                          <span className="bg-orange-100 text-orange-700 text-xs px-2 py-0.5 rounded-full">HuggingFace</span>
                          <span className="text-xs text-gray-500">{m.hfMeta.modelId}</span>
                          <span className="text-xs text-gray-400">Downloaded {new Date(m.hfMeta.downloadedAt).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })}</span>
                        </div>
                      )}
                    </td>
                    <td className="px-4 py-3 text-xs text-gray-500 truncate max-w-[200px]" title={m.fullPath.substring(0, m.fullPath.lastIndexOf('/'))}>
                      {m.directoryName ?? m.fullPath.substring(0, m.fullPath.lastIndexOf('/')).split('/').pop()}
                    </td>
                    <td className="px-4 py-3 text-gray-600">{fmtBytes(m.sizeBytes)}</td>
                    <td className="px-4 py-3 text-gray-600">{fmtDate(m.modifiedAt)}</td>
                    <td className="px-4 py-3">
                      <button
                        onClick={() => handleCopyPath(m.fullPath)}
                        title="Copy full path"
                        className={`flex items-center gap-1 px-2 py-1 rounded text-xs transition-colors ${
                          copiedPath === m.fullPath
                            ? 'bg-green-100 text-green-700'
                            : 'bg-gray-100 text-gray-600 hover:bg-gray-200'
                        }`}
                      >
                        <Copy size={12} />
                        {copiedPath === m.fullPath ? 'Copied!' : 'Copy path'}
                      </button>
                    </td>
                  </tr>
                ))}
                {(!localModels || localModels.length === 0) && (
                  <tr>
                    <td colSpan={5} className="px-4 py-8 text-center text-gray-400">
                      No local models found. Download a model below to get started.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {}
      {activeDownloads.size > 0 && (
        <section className="space-y-3">
          <h3 className="text-lg font-semibold text-gray-700 flex items-center gap-2">
            <Download size={18} className="text-[#4a9eed]" />
            Active Downloads
          </h3>
          <div className="space-y-2">
            {Array.from(activeDownloads.entries()).map(([downloadId, progress]) => (
              <DownloadRow
                key={downloadId}
                downloadId={downloadId}
                progress={progress}
                onCancel={handleCancelDownload}
              />
            ))}
          </div>
        </section>
      )}

      {}
      <section className="space-y-3">
        <h3 className="text-lg font-semibold text-gray-700 flex items-center gap-2">
          <Search size={18} className="text-[#4a9eed]" />
          HuggingFace Search
        </h3>

        <form onSubmit={handleSearch} className="flex gap-2">
          <input
            value={searchInput}
            onChange={e => setSearchInput(e.target.value)}
            placeholder="Search models (e.g. TheBloke Llama-2 GGUF)…"
            className="flex-1 border rounded px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-[#4a9eed]"
          />
          <button
            type="submit"
            disabled={searchInput.trim().length === 0}
            className="flex items-center gap-1 px-4 py-2 bg-[#4a9eed] text-white rounded text-sm hover:bg-[#3a8edd] disabled:opacity-50 transition-colors"
          >
            <Search size={14} /> Search
          </button>
        </form>

        {searchLoading && (
          <div className="flex items-center justify-center py-8">
            <Loader2 className="animate-spin text-[#4a9eed]" size={28} />
          </div>
        )}
        {searchError && (
          <div className="p-4 bg-red-50 border border-red-200 rounded text-red-700 text-sm">
            Search failed: {(searchError as Error).message}
          </div>
        )}
        {!searchLoading && searchResults && searchResults.length === 0 && (
          <div className="p-8 text-center text-gray-400 bg-white rounded-lg shadow">
            No results found for "{searchQuery}".
          </div>
        )}

        {}
        {!searchLoading && searchResults && searchResults.length > 0 && (
          <div className={`flex gap-4 items-start ${selectedModel ? 'flex-col xl:flex-row' : ''}`}>
            {}
            <div className={`${selectedModel ? 'xl:w-1/2 w-full' : 'w-full'}`}>
              <div className={`grid gap-3 items-stretch ${selectedModel ? 'grid-cols-1 sm:grid-cols-2' : 'grid-cols-1 sm:grid-cols-2 lg:grid-cols-3'}`}>
                {searchResults.map((result: HfSearchResult) => (
                  <div
                    key={result.modelId}
                    className={`bg-white rounded-lg border p-4 flex flex-col h-full hover:shadow-sm transition-all cursor-pointer ${
                      selectedModel?.modelId === result.modelId
                        ? 'border-blue-400'
                        : 'border-gray-200 hover:border-blue-300'
                    }`}
                    onClick={() => setSelectedModel(result)}
                  >
                    {}
                    <h3 className="font-semibold text-gray-900 truncate text-sm" title={result.modelName}>
                      {result.modelName}
                    </h3>
                    {}
                    <p className="text-xs text-gray-500 truncate mt-0.5" title={result.author}>
                      by {result.author}
                    </p>

                    {}
                    <div className="flex items-center gap-3 mt-2 text-xs text-gray-500">
                      <span className="flex items-center gap-1">
                        <Download className="w-3 h-3" />
                        {fmtDownloads(result.downloads)}
                      </span>
                      <span className="flex items-center gap-1">
                        <Heart className="w-3 h-3" />
                        {result.likes}
                      </span>
                      <a
                        href={`https://huggingface.co/${result.modelId}`}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="ml-auto text-gray-400 hover:text-[#4a9eed] flex-shrink-0"
                        title="Open on HuggingFace"
                        onClick={e => e.stopPropagation()}
                      >
                        <ExternalLink size={12} />
                      </a>
                    </div>

                    {}
                    <div className="flex items-center gap-1 mt-2 h-5 overflow-hidden">
                      {result.tags.slice(0, 3).map(tag => (
                        <span key={tag} className="px-1.5 py-0.5 bg-blue-50 text-blue-700 rounded text-xs truncate max-w-[80px]">
                          {tag}
                        </span>
                      ))}
                      {result.tags.length > 3 && (
                        <span className="text-xs text-gray-400 flex-shrink-0">+{result.tags.length - 3}</span>
                      )}
                    </div>

                    {}
                    <div className="flex-1" />

                    {}
                    <button
                      className={`mt-3 w-full py-1.5 px-3 rounded text-xs font-medium transition-colors ${
                        selectedModel?.modelId === result.modelId
                          ? 'bg-blue-600 text-white'
                          : 'bg-gray-100 text-gray-700 hover:bg-blue-50 hover:text-blue-700'
                      }`}
                      onClick={e => { e.stopPropagation(); setSelectedModel(result) }}
                    >
                      {selectedModel?.modelId === result.modelId ? 'Viewing files' : 'View files'}
                    </button>
                  </div>
                ))}
              </div>
            </div>

            {}
            {selectedModel && (
              <div ref={detailPanelRef} className="xl:w-1/2 w-full xl:sticky xl:top-4 space-y-3">
                <div className="flex items-center justify-between">
                  <h3 className="text-base font-semibold text-gray-700 flex items-center gap-2">
                    <HardDrive size={16} className="text-[#4a9eed]" />
                    Model Files
                  </h3>
                  <button
                    onClick={() => setSelectedModel(null)}
                    className="text-gray-400 hover:text-gray-600 p-1"
                    title="Close"
                  >
                    <X size={18} />
                  </button>
                </div>

                <div className="bg-white rounded-lg shadow p-4 space-y-4">
                  {}
                  <div className="flex items-start justify-between gap-4">
                    <div className="space-y-1 min-w-0">
                      <h4 className="font-semibold text-gray-800 truncate">{selectedModel.modelName}</h4>
                      <p className="text-sm text-gray-500">by {selectedModel.author}</p>
                      <div className="flex flex-wrap items-center gap-3 text-xs text-gray-500">
                        <span>↓ {fmtDownloads(selectedModel.downloads)} downloads</span>
                        <span>♥ {fmtDownloads(selectedModel.likes)} likes</span>
                        {selectedModel.lastModified && (
                          <span>Updated {fmtDate(selectedModel.lastModified)}</span>
                        )}
                      </div>
                    </div>
                    <a
                      href={`https://huggingface.co/${selectedModel.modelId}`}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="flex items-center gap-1 px-2 py-1.5 bg-gray-100 text-gray-700 rounded text-xs hover:bg-gray-200 transition-colors flex-shrink-0"
                    >
                      <ExternalLink size={12} /> HF
                    </a>
                  </div>

                  {}
                  {modelDirectories && modelDirectories.length > 0 && (
                    <div className="flex items-center gap-2 text-sm">
                      <label className="text-gray-500 whitespace-nowrap">Download to:</label>
                      <select
                        value={selectedDownloadDir}
                        onChange={e => setSelectedDownloadDir(e.target.value)}
                        className="flex-1 border rounded px-2 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-[#4a9eed]"
                      >
                        {modelDirectories.map(d => (
                          <option key={d.id} value={d.path}>{d.name}</option>
                        ))}
                      </select>
                    </div>
                  )}

                  {}
                  {detailLoading && (
                    <div className="flex items-center justify-center py-6">
                      <Loader2 className="animate-spin text-[#4a9eed]" size={24} />
                    </div>
                  )}
                  {detailError && (
                    <div className="p-3 bg-red-50 border border-red-200 rounded text-red-700 text-sm">
                      Failed to load model files: {(detailError as Error).message}
                    </div>
                  )}
                  {!detailLoading && modelDetail && (
                    <div className="overflow-hidden rounded border border-gray-200">
                      <table className="w-full text-sm">
                        <thead className="bg-gray-50 border-b">
                          <tr>
                            <th className="text-left px-3 py-2.5 font-medium text-gray-600 text-xs">Filename</th>
                            <th className="text-left px-3 py-2.5 font-medium text-gray-600 text-xs">Size</th>
                            <th className="text-left px-3 py-2.5 font-medium text-gray-600 text-xs">Action</th>
                          </tr>
                        </thead>
                        <tbody>
                          {modelDetail.files.length === 0 && (
                            <tr>
                              <td colSpan={3} className="px-4 py-6 text-center text-gray-400">
                                No GGUF files found for this model.
                              </td>
                            </tr>
                          )}
                          {modelDetail.files.map(file => {
                            const alreadyDownloading = activeDownloads.has(`${selectedModel.modelId}/${file.filename}`)
                            const alreadyDownloaded = localModels?.some(m => m.hfMeta?.modelId === selectedModel.modelId && m.hfMeta?.filename === file.filename) ?? false
                            return (
                              <tr key={file.filename} className="border-b last:border-b-0 hover:bg-blue-50 transition-colors">
                                <td className="px-3 py-2.5 font-mono text-xs text-gray-800 max-w-[200px] truncate" title={file.filename}>{file.filename}</td>
                                <td className="px-3 py-2.5 text-xs text-gray-600 whitespace-nowrap">{fmtBytes(file.sizeBytes ?? undefined)}</td>
                                <td className="px-3 py-2.5">
                                  {alreadyDownloaded ? (
                                    <span className="inline-flex items-center gap-1 bg-green-100 text-green-700 text-xs px-2 py-0.5 rounded-full">
                                      <CheckCircle2 size={11} /> Downloaded
                                    </span>
                                  ) : (
                                    <button
                                      onClick={() => handleDownload(selectedModel.modelId, file.filename, file.sizeBytes ?? undefined, {
                                        author: selectedModel.author,
                                        description: selectedModel.description,
                                        tags: selectedModel.tags,
                                        hfDownloads: selectedModel.downloads,
                                        hfLikes: selectedModel.likes,
                                      }, selectedDownloadDir || undefined)}
                                      disabled={alreadyDownloading}
                                      className="flex items-center gap-1 px-2.5 py-1.5 bg-[#4a9eed] text-white rounded text-xs hover:bg-[#3a8edd] disabled:opacity-50 transition-colors whitespace-nowrap"
                                    >
                                      <Download size={11} />
                                      {alreadyDownloading ? 'Downloading…' : 'Download'}
                                    </button>
                                  )}
                                </td>
                              </tr>
                            )
                          })}
                        </tbody>
                      </table>
                    </div>
                  )}
                </div>
              </div>
            )}
          </div>
        )}
      </section>
    </div>
  )
}

export default Models
