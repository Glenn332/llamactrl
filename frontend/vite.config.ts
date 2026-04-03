import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../src/LlamaCtrl/wwwroot',
    emptyOutDir: true,
  },
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:3131',
        changeOrigin: true,
        // Disable response buffering so SSE events arrive immediately in dev mode
        configure: (proxy) => {
          proxy.on('proxyRes', (proxyRes) => {
            if (proxyRes.headers['content-type']?.includes('text/event-stream')) {
              proxyRes.socket.setNoDelay(true)
            }
          })
        },
      },
      '/hubs': {
        target: 'http://localhost:3131',
        ws: true,
        changeOrigin: true,
      },
    },
  },
})
