import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../wwwroot/ContentSeeder',
    emptyOutDir: true,
    rollupOptions: {
      output: {
        entryFileNames: 'assets/index.js',
        chunkFileNames: 'assets/[name].js',
        assetFileNames: 'assets/[name][extname]',
      },
    },
  },
  // Relative, not a hardcoded absolute prefix — the module's assets sit next to index.html
  // wherever the CMS's module-resource-serving convention mounts it, and that convention has
  // already changed once (CMS 12's /EPiServer/{module}/... vs CMS 13's /Optimizely/{module}/...).
  base: './',
})
