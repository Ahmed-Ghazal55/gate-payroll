/// <reference types="vitest" />
import { defineConfig } from 'vite';

export default defineConfig({
  base: './',
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    target: 'es2022'
  },
  server: {
    port: 3000
  },
  test: {
    environment: 'happy-dom'
  }
});
