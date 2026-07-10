import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5406',
        changeOrigin: true,
      },
      // Le hub SignalR de suivi d'ingestion doit être proxifié comme /api, sinon la négociation
      // WebSocket part vers le serveur Vite lui-même (échec silencieux) et la page de détail
      // document ne reçoit jamais les mises à jour de pourcentage en direct.
      '/hubs': {
        target: 'http://localhost:5406',
        changeOrigin: true,
        ws: true,
      },
    },
  },
})
