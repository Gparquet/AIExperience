import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'https://localhost:50405',
        changeOrigin: true,
        secure: false, // certificat auto-signé en développement
      },
    },
  },
})
