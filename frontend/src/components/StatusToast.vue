<script setup>
// Install progress / errors, floating under the caption bar.
import { computed } from 'vue'
import { motion, AnimatePresence } from 'motion-v'
import AppIcon from './AppIcon.vue'
import { state, send, busyPhases } from '../state'
import { popSpring, reducedMotion } from '../motion'

const toast = computed(() => {
  const g = state.game
  if (g.phase === 'error') return { key: 'error', tone: 'error', icon: 'CircleAlert', title: 'Something went wrong', text: g.message, dismiss: () => send('game.dismiss') }
  if (state.account.error) return { key: 'auth', tone: 'error', icon: 'CircleAlert', title: 'Sign-in', text: state.account.error, dismiss: () => send('auth.dismiss') }
  if (state.notice) return { key: 'notice', tone: 'info', icon: 'Info', title: 'Heads up', text: state.notice, dismiss: () => state.notice = null }
  if (busyPhases.includes(g.phase)) return { key: 'progress', tone: 'progress', icon: 'Download', title: g.message, text: g.detail, progress: g.progress }
  if (state.launcher.updating) return { key: 'updating', tone: 'progress', icon: 'Download', title: 'Updating the launcher', text: 'A new version is downloading. The launcher restarts by itself.' }
  if (g.notice) return { key: 'installed', tone: 'ok', icon: 'CircleCheck', title: g.notice, text: state.account.signedIn ? '' : 'Sign in with Microsoft to play.', dismiss: () => send('game.dismiss') }
  if (g.phase === 'running') return { key: 'running', tone: 'ok', icon: 'CircleCheck', title: g.message, text: 'Have fun! You can close the launcher at any time.' }
  return null
})
</script>
<template>
<AnimatePresence>
  <motion.div v-if="toast" :key="toast.key" class="toast" :class="toast.tone" role="status"
    :initial="{ opacity: 0, y: reducedMotion ? 0 : -14, scale: .97 }" :animate="{ opacity: 1, y: 0, scale: 1 }" :exit="{ opacity: 0, y: reducedMotion ? 0 : -10, scale: .98 }" :transition="popSpring">
    <AppIcon :name="toast.icon" :size="18" class="toast-icon"/>
    <div class="toast-body">
      <strong>{{ toast.title }}</strong>
      <span v-if="toast.text">{{ toast.text }}</span>
      <div v-if="toast.progress !== undefined" class="toast-bar"><motion.i :animate="{ scaleX: Math.max(.02, toast.progress) }" :initial="false" :transition="{ type: 'spring', stiffness: 120, damping: 24 }"/></div>
    </div>
    <button v-if="toast.dismiss" class="icon-button" aria-label="Dismiss" @click="toast.dismiss()"><AppIcon name="X" :size="15"/></button>
  </motion.div>
</AnimatePresence>
</template>
