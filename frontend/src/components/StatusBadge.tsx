interface Props { status: string }
const colors: Record<string, string> = {
  Running: 'bg-green-900 border-green-500 text-green-300',
  Starting: 'bg-yellow-900 border-yellow-500 text-yellow-300',
  Stopped: 'bg-gray-700 border-gray-500 text-gray-300',
  Error: 'bg-red-900 border-red-500 text-red-300',
}
export function StatusBadge({ status }: Props) {
  return (
    <span className={`px-2 py-0.5 rounded border text-xs font-medium ${colors[status] ?? colors.Stopped}`}>
      {status}
    </span>
  )
}
