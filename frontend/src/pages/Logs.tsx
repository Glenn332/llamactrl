import { useState, useRef, useEffect } from 'react'
import { useQuery } from '@tanstack/react-query'
import { instancesApi } from '../api/instances'
import { useLogHub } from '../hooks/useSignalR'
import { Loader2, ArrowDown, ArrowUp, Download, Trash2 } from 'lucide-react'

type LogLevel = 'All' | 'INFO' | 'WARN' | 'ERROR'

function getLogLevel(line: string): 'INFO' | 'WARN' | 'ERROR' {
  if (/\bERROR\b/i.test(line)) return 'ERROR'
  if (/\bWARN(ING)?\b/i.test(line)) return 'WARN'
  return 'INFO'
}

function getLogColor(level: 'INFO' | 'WARN' | 'ERROR') {
  switch (level) {
    case 'ERROR': return 'text-red-400'
    case 'WARN': return 'text-yellow-400'
    default: return 'text-gray-200'
  }
}

export function Logs() {
  const [selectedInstance, setSelectedInstance] = useState<number | null>(null)
  const [levelFilter, setLevelFilter] = useState<LogLevel>('All')
  const [search, setSearch] = useState('')
  const [autoScroll, setAutoScroll] = useState(true)
  const scrollRef = useRef<HTMLDivElement>(null)

  const { data: instances, isLoading } = useQuery({
    queryKey: ['instances'],
    queryFn: instancesApi.getAll,
  })

  const { logs, clearLogs } = useLogHub(selectedInstance)

  const filtered = logs.filter(line => {
    if (levelFilter !== 'All' && getLogLevel(line) !== levelFilter) return false
    if (search && !line.toLowerCase().includes(search.toLowerCase())) return false
    return true
  })

  const errorCount = logs.filter(l => getLogLevel(l) === 'ERROR').length
  const warnCount = logs.filter(l => getLogLevel(l) === 'WARN').length

  useEffect(() => {
    if (autoScroll && scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight
    }
  }, [filtered.length, autoScroll])

  const handleSave = () => {
    const blob = new Blob([filtered.join('\n')], { type: 'text/plain' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url; a.download = `logs-instance-${selectedInstance}.txt`; a.click()
    URL.revokeObjectURL(url)
  }

  if (isLoading) return <div className="flex items-center justify-center h-full"><Loader2 className="animate-spin" size={32} /></div>

  return (
    <div className="p-6 flex flex-col h-full gap-4">
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-bold text-gray-800">Logs</h2>
      </div>

      {/* Controls */}
      <div className="flex flex-wrap items-center gap-3">
        <select value={selectedInstance ?? ''} onChange={e => { setSelectedInstance(Number(e.target.value) || null); clearLogs() }}
          className="border rounded px-3 py-1.5 text-sm">
          <option value="">Select instance...</option>
          {instances?.map(i => <option key={i.id} value={i.id}>{i.name} ({i.status})</option>)}
        </select>

        <div className="flex border rounded overflow-hidden text-sm">
          {(['All', 'INFO', 'WARN', 'ERROR'] as LogLevel[]).map(level => (
            <button key={level} onClick={() => setLevelFilter(level)}
              className={`px-3 py-1.5 transition-colors ${levelFilter === level ? 'bg-[#4a9eed] text-white' : 'bg-white text-gray-600 hover:bg-gray-100'}`}>
              {level}
            </button>
          ))}
        </div>

        <input placeholder="Search logs..." value={search} onChange={e => setSearch(e.target.value)}
          className="border rounded px-3 py-1.5 text-sm w-48" />

        <label className="flex items-center gap-1.5 text-sm text-gray-600">
          <input type="checkbox" checked={autoScroll} onChange={e => setAutoScroll(e.target.checked)} />
          Auto-scroll
        </label>
      </div>

      {/* Log viewer */}
      <div ref={scrollRef} className="flex-1 bg-gray-900 rounded-lg p-4 overflow-auto font-mono text-xs min-h-0">
        {!selectedInstance && <p className="text-gray-500">Select an instance to view logs.</p>}
        {selectedInstance && filtered.length === 0 && <p className="text-gray-500">No log entries{levelFilter !== 'All' || search ? ' matching filters' : ''}.</p>}
        {filtered.map((line, i) => {
          const level = getLogLevel(line)
          return <div key={i} className={`py-0.5 ${getLogColor(level)}`}>{line}</div>
        })}
      </div>

      {/* Footer controls */}
      <div className="flex items-center justify-between text-sm">
        <div className="flex gap-4 text-gray-600">
          <span>{filtered.length} lines</span>
          <span className="text-red-600">{errorCount} errors</span>
          <span className="text-yellow-600">{warnCount} warnings</span>
        </div>
        <div className="flex gap-2">
          <button onClick={() => scrollRef.current && (scrollRef.current.scrollTop = 0)}
            className="flex items-center gap-1 px-2 py-1 bg-gray-200 text-gray-700 rounded text-xs hover:bg-gray-300">
            <ArrowUp size={12} /> Top
          </button>
          <button onClick={() => scrollRef.current && (scrollRef.current.scrollTop = scrollRef.current.scrollHeight)}
            className="flex items-center gap-1 px-2 py-1 bg-gray-200 text-gray-700 rounded text-xs hover:bg-gray-300">
            <ArrowDown size={12} /> Bottom
          </button>
          <button onClick={handleSave}
            className="flex items-center gap-1 px-2 py-1 bg-gray-200 text-gray-700 rounded text-xs hover:bg-gray-300">
            <Download size={12} /> Save to File
          </button>
          <button onClick={clearLogs}
            className="flex items-center gap-1 px-2 py-1 bg-gray-200 text-gray-700 rounded text-xs hover:bg-gray-300">
            <Trash2 size={12} /> Clear
          </button>
        </div>
      </div>
    </div>
  )
}
