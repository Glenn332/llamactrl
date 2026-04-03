export type ParamType = 'number' | 'text' | 'flag' | 'select'

export interface ParamCatalogEntry {
  flag: string
  label: string
  type: ParamType
  options?: string[]
  description: string
  defaultValue?: string
  step?: string
}

export const LLAMA_PARAM_CATALOG: ParamCatalogEntry[] = [
  { flag: '-c', label: 'Context Size', type: 'number', description: 'Maximum context window in tokens', defaultValue: '4096' },
  { flag: '-b', label: 'Batch Size', type: 'number', description: 'Batch size for prompt processing', defaultValue: '512' },
  { flag: '-ngl', label: 'GPU Layers', type: 'number', description: 'Number of layers to offload to GPU (0 = CPU only)', defaultValue: '0' },
  { flag: '-t', label: 'Threads', type: 'number', description: 'CPU threads for generation', defaultValue: '8' },
  { flag: '-fa', label: 'Flash Attention', type: 'select', options: ['on', 'off'], description: 'Enable Flash Attention (requires compatible hardware)', defaultValue: 'off' },
  { flag: '--no-mmap', label: 'Disable Memory Map', type: 'flag', description: 'Load model fully into RAM instead of memory-mapping' },
  { flag: '--mlock', label: 'Memory Lock', type: 'flag', description: 'Lock model in RAM, preventing swap to disk' },
  { flag: '--temp', label: 'Temperature', type: 'number', step: '0.05', description: 'Sampling temperature (higher = more creative)', defaultValue: '0.8' },
  { flag: '--top-p', label: 'Top P', type: 'number', step: '0.05', description: 'Nucleus sampling threshold', defaultValue: '0.9' },
  { flag: '--top-k', label: 'Top K', type: 'number', description: 'Top-K sampling cutoff', defaultValue: '40' },
{ flag: '--parallel', label: 'Parallel Slots', type: 'number', description: 'Simultaneous inference slots', defaultValue: '1' },
  { flag: '--host', label: 'Host', type: 'text', description: 'Bind address (default: 127.0.0.1)', defaultValue: '127.0.0.1' },
  { flag: '--log-disable', label: 'Disable Logging', type: 'flag', description: 'Suppress llama-server log output' },
  { flag: '-s', label: 'Seed', type: 'number', description: 'RNG seed (-1 = random)', defaultValue: '-1' },
  { flag: '--rope-scaling', label: 'RoPE Scaling', type: 'select', options: ['none', 'linear', 'yarn'], description: 'RoPE frequency scaling method', defaultValue: 'none' },
  { flag: '--keep', label: 'Keep Tokens', type: 'number', description: 'Tokens from initial prompt to retain when context fills', defaultValue: '0' },
  { flag: '--ubatch-size', label: 'Micro Batch Size', type: 'number', description: 'Physical batch size for computation', defaultValue: '512' },
  { flag: '--draft', label: 'Draft Tokens', type: 'number', description: 'Speculative decoding draft tokens', defaultValue: '5' },
]
