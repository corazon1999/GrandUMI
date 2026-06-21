import { createApp } from 'vue'
import './style.css'
import App from './App.vue'
import { router } from './router'
import { useNetStore } from './store/netStore'

// 在 mount 之前应用主题 + 气质（避免 FOUC）
;(function applyTheme() {
  try {
    const saved = localStorage.getItem('grandumi-theme')
    const theme = saved === 'marine' ? 'marine' : 'pirate'
    document.documentElement.dataset.theme = theme
    // 气质 mood 持久化（a=终端 b=电影 c=游戏）
    const mood = localStorage.getItem('grandumi-mood') || 'b'
    document.documentElement.dataset.mood = mood
  } catch {
    document.documentElement.dataset.theme = 'pirate'
    document.documentElement.dataset.mood = 'b'
  }
})()

// DEV: 暴露 store 到 window 以便自动化测试注入状态（仅 vite dev 模式）
if (import.meta.env.DEV) {
  ;(window as unknown as { __netStore: typeof useNetStore }).__netStore = useNetStore
}

createApp(App).use(router).mount('#app')
