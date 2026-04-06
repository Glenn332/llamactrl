import { Link, useLocation, Outlet } from 'react-router-dom'
import { useSystemStatus } from '../hooks/useSystemStatus'
import { Server, Layers, HardDrive, Activity, ScrollText, Settings } from 'lucide-react'

const navItems = [
  { path: '/', label: 'Instances', icon: Server },
  { path: '/profiles', label: 'Profiles', icon: Layers },
  { path: '/models', label: 'Models', icon: HardDrive },
  { path: '/benchmarks', label: 'Benchmarks', icon: Activity },
  { path: '/logs', label: 'Logs', icon: ScrollText },
  { path: '/settings', label: 'Settings', icon: Settings },
]

export function Layout() {
  const location = useLocation()
  const { data: status } = useSystemStatus()

  return (
    <div className="flex h-screen">
      {/* Sidebar */}
      <aside className="w-52 flex-shrink-0 bg-[#1e3a5f] border-r border-[#4a9eed] flex flex-col">
        <div className="p-4 border-b border-[#4a9eed]">
          <h1 className="text-white text-xl font-bold">LlamaCtrl</h1>
        </div>
        <nav className="flex-1 p-2 space-y-1">
          {navItems.map(({ path, label, icon: Icon }) => (
            <Link key={path} to={path}
              className={`flex items-center gap-2 px-3 py-2 rounded text-sm transition-colors ${
                location.pathname === path
                  ? 'bg-[#4a9eed] text-white'
                  : 'text-gray-300 hover:bg-[#254875] hover:text-white'
              }`}>
              <Icon size={16} />
              {label}
            </Link>
          ))}
        </nav>
        {/* System status */}
        <div className="p-3 border-t border-[#4a9eed] space-y-2 text-xs">
          <p className="text-gray-400 font-medium">System Status</p>
          <div className="bg-green-900 border border-green-500 rounded px-2 py-1 text-green-300">
            CPU {status ? `${status.cpuPercent.toFixed(0)}%` : '--'}
          </div>
          <div className="bg-green-900 border border-green-500 rounded px-2 py-1 text-green-300">
            RAM {status ? `${status.ramUsedGb.toFixed(1)} / ${status.ramTotalGb.toFixed(0)} GB` : '--'}
          </div>
<div className="bg-orange-900 border border-orange-500 rounded px-2 py-1 text-orange-300">
            Active: {status?.activeInstances ?? 0} instances
          </div>
        </div>
      </aside>
      {/* Main content */}
      <main className="flex-1 overflow-auto bg-[#f0f4ff]">
        <Outlet />
      </main>
    </div>
  )
}
