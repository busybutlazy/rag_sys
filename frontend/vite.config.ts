import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': { target: 'http://be-server:8001', rewrite: (p) => p.replace(/^\/api/, '') },
      '/ai':  { target: 'http://ai-server:8002',  rewrite: (p) => p.replace(/^\/ai/, '') },
    }
  }
})
