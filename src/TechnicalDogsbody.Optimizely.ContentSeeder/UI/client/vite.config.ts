import { defineConfig, type Plugin } from 'vite'
import react from '@vitejs/plugin-react'

// The committed UI is diffed against the Linux CI build, so the built HTML must be LF on every platform. Vite copies
// line endings from index.html, so a CRLF working copy would otherwise produce CRLF output.
const lfHtml: Plugin = {
  name: 'lf-html',
  apply: 'build',
  // Runs after Vite has written index.html, so it sees the final content.
  generateBundle: {
    order: 'post',
    handler(_, bundle) {
      for (const file of Object.values(bundle)) {
        if (file.type === 'asset' && file.fileName.endsWith('.html') && typeof file.source === 'string') {
          file.source = file.source.replace(/\r/g, '')
        }
      }
    },
  },
}

export default defineConfig({
  plugins: [react(), lfHtml],
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
