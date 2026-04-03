export interface Instance {
  id: number; name: string; profileId: number; profileName: string;
  port: number; pid?: number; status: 'Stopped' | 'Starting' | 'Running' | 'Error';
  createdAt: string; updatedAt: string; metrics?: Metrics; uptime?: string;
}
export interface Metrics {
  tokensPerSec: number; avgLatencyMs: number; totalRequests: number; vramUsedMb: number;
}
export interface ProfileParameter {
  flag: string
  value: string
}

export interface Profile {
  id: number
  name: string
  modelPath: string
  selectedBinaryId: number | null
  parameters: Record<string, string>  // { "-c": "4096", "--mlock": "" }
  customArgs: ProfileParameter[]       // [{ flag: "--foo", value: "bar" }]
  createdAt: string
  updatedAt: string
}
export interface AgentRoundResult {
  round: number;
  inputTokens: number;
  outputTokens: number;
  ttftMs: number;
  speedTps: number;
}

export interface BenchmarkResult {
  id: number; profileId: number; profileName: string; instanceId?: number;
  runAt: string; generationSpeedTps: number; promptSpeedTps: number;
  timeToFirstTokenMs: number; vramUsedMb: number; notes?: string;
  chartDataJson?: string;
  benchmarkType: string;  // "token-generation" | "agentic"
  rounds?: AgentRoundResult[];
}
export interface SystemStatus {
  cpuPercent: number; ramUsedGb: number; ramTotalGb: number;
  vramUsedGb: number; vramTotalGb: number; activeInstances: number;
}
export interface AppSettings {
  port: number; dataDir: string; modelsDir: string;
  openBrowserOnStart: boolean; relaunchOnStartup: boolean; healthPollIntervalSeconds: number;
}

export interface HfSearchResult {
  modelId: string
  author: string
  modelName: string
  downloads: number
  likes: number
  tags: string[]
  description?: string
  lastModified?: string
}

export interface HfFileInfo {
  filename: string
  sizeBytes?: number
  downloadUrl: string
}

export interface HfModelInfo extends HfSearchResult {
  files: HfFileInfo[]
}

export interface HfMeta {
  modelId: string
  filename: string
  author: string | null
  description: string | null
  tags: string[] | null
  downloadedAt: string
  sizeBytes: number | null
  hfDownloads: number | null
  hfLikes: number | null
}

export interface LocalModel {
  filename: string
  fullPath: string
  sizeBytes: number
  modifiedAt: string
  hfMeta: HfMeta | null
  directoryName: string | null
}

export interface DownloadProgress {
  downloadId: string
  filename: string
  phase: 'starting' | 'downloading' | 'done' | 'error' | 'cancelled'
  bytesReceived: number
  totalBytes?: number
  percentComplete: number
  error?: string
  isComplete: boolean
}

export interface LlamaServerBinary {
  id: number
  name: string
  path: string
  isDefault: boolean
  createdAt: string
  updatedAt: string
}

export interface ModelDirectory {
  id: number
  name: string
  path: string
  createdAt: string
  updatedAt: string
}
