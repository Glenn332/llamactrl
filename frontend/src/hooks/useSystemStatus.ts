import { useQuery } from '@tanstack/react-query'
import { apiGet } from '../api/client'
import type { SystemStatus } from '../api/types'

export function useSystemStatus() {
  return useQuery({
    queryKey: ['systemStatus'],
    queryFn: () => apiGet<SystemStatus>('/system/status'),
    refetchInterval: 5000,
  })
}
