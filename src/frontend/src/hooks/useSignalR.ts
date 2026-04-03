import { useEffect, useRef, useState } from 'react'
import * as signalR from '@microsoft/signalr'
import type { DownloadProgress } from '../api/types'

export function useLogHub(instanceId: number | null) {
  const [logs, setLogs] = useState<string[]>([])
  const connectionRef = useRef<signalR.HubConnection | null>(null)

  useEffect(() => {
    if (!instanceId) return

    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/logs')
      .withAutomaticReconnect()
      .build()

    connectionRef.current = connection

    connection.on('ReceiveLog', (id: number, line: string) => {
      if (id === instanceId)
        setLogs(prev => [...prev.slice(-999), line])
    })

    connection.start().then(() => connection.invoke('JoinInstance', instanceId.toString()))

    return () => {
      connection.invoke('LeaveInstance', instanceId.toString()).then(() => connection.stop())
    }
  }, [instanceId])

  return { logs, clearLogs: () => setLogs([]) }
}

export function useMetricsHub(onInstanceStatus?: (id: number, status: string) => void) {
  const connectionRef = useRef<signalR.HubConnection | null>(null)

  useEffect(() => {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/metrics')
      .withAutomaticReconnect()
      .build()
    connectionRef.current = connection

    if (onInstanceStatus)
      connection.on('InstanceStatusChanged', onInstanceStatus)

    connection.start().then(() => connection.invoke('JoinMetrics'))
    return () => { connection.stop() }
  }, [])
}

export function useDownloadHub(
  downloadId: string | null,
  onProgress: (progress: DownloadProgress) => void
) {
  const onProgressRef = useRef(onProgress)
  onProgressRef.current = onProgress

  useEffect(() => {
    if (!downloadId) return

    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/downloads')
      .withAutomaticReconnect()
      .build()

    connection.on('DownloadProgress', (progress: DownloadProgress) => {
      onProgressRef.current(progress)
    })

    connection.start().then(() => {
      connection.invoke('JoinDownload', downloadId).catch(console.error)
    }).catch(console.error)

    return () => {
      connection.invoke('LeaveDownload', downloadId)
        .catch(console.error)
        .finally(() => connection.stop())
    }
  }, [downloadId])
}
