import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // Puerto fijo: coincide con el origen autorizado en CORS de template.local.yaml
    // (Globals.HttpApi.CorsConfiguration). Si esta puerto ya está en uso, falla en vez
    // de saltar silenciosamente a otro (lo que rompería CORS contra la API local).
    port: 5173,
    strictPort: true,
  },
})
