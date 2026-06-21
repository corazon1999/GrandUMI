import { defineConfig } from 'vite'
import { fileURLToPath, URL } from 'node:url'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [vue(), tailwindcss()],
  resolve: {
    alias: {
      // 与旧项目一致：@/* → src/*
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    // 对齐旧项目 `next dev -H 0.0.0.0`，便于局域网/手机调试
    host: '0.0.0.0',
  },
})
