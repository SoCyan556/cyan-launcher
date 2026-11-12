import { createApp } from 'vue'
import App from './App.vue'
import './style.css'

function syncVisibility() { document.documentElement.classList.toggle('app-hidden', document.hidden) }
document.addEventListener('visibilitychange', syncVisibility)
syncVisibility()

createApp(App).mount('#app')
