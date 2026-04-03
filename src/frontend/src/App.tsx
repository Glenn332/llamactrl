import { Routes, Route } from 'react-router-dom'
import { Layout } from './components/Layout'
import { Instances } from './pages/Instances'
import { Profiles } from './pages/Profiles'
import Models from './pages/Models'
import { Benchmarks } from './pages/Benchmarks'
import { Logs } from './pages/Logs'
import { SettingsPage } from './pages/Settings'

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<Layout />}>
        <Route index element={<Instances />} />
        <Route path="profiles" element={<Profiles />} />
        <Route path="models" element={<Models />} />
        <Route path="benchmarks" element={<Benchmarks />} />
        <Route path="logs" element={<Logs />} />
        <Route path="settings" element={<SettingsPage />} />
      </Route>
    </Routes>
  )
}
