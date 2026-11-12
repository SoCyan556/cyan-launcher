<script setup>
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { motion, AnimatePresence } from 'motion-v'
import AppIcon from './AppIcon.vue'
import { state, send, busyPhases } from '../state'
import { spring, popSpring, press, reducedMotion } from '../motion'

const open = ref(false), root = ref(null)
const busy = computed(() => busyPhases.includes(state.game.phase))
const running = computed(() => state.game.phase === 'running')
// Animate the label only when the button changes meaning, not on every progress tick.
// The first press installs the whole pack in one go (no sign-in needed); after that it's Play / Update & Play.
const installed = computed(() => state.modpack.installed)
const mode = computed(() => busy.value ? state.game.phase : running.value ? 'running' : !installed.value ? 'install'
  : !state.account.signedIn ? (state.account.busy ? 'signing' : 'signin') : state.modpack.updateAvailable ? 'update' : 'play')
const percent = computed(() => Math.round(state.game.progress * 100))
const label = computed(() => {
  if (busy.value) return state.game.phase === 'launching' ? 'Launching…' : `${percent.value}%`
  if (running.value) return 'Playing'
  if (!installed.value) return 'Install modpack'
  if (!state.account.signedIn) return state.account.busy ? 'Signing in…' : 'Sign in to play'
  return state.modpack.updateAvailable ? 'Update & Play' : 'Play'
})

function primary() {
  open.value = false
  if (busy.value || running.value) return
  if (!installed.value) send('game.install')
  else if (!state.account.signedIn) send('auth.signIn')
  else send('game.play')
}
function secondary() {
  if (busy.value && state.game.canCancel) send('game.cancel')
  else open.value = !open.value
}
function pick(type, payload) { open.value = false; send(type, payload) }
function outside(e) { if (open.value && root.value && !root.value.contains(e.target)) open.value = false }
function escape(e) { if (e.key === 'Escape') open.value = false }
onMounted(() => { document.addEventListener('pointerdown', outside); window.addEventListener('keydown', escape) })
onBeforeUnmount(() => { document.removeEventListener('pointerdown', outside); window.removeEventListener('keydown', escape) })
</script>
<template>
<div ref="root" class="play-wrap">
  <motion.div class="play-button" :class="{ busy, running, signin: installed && !state.account.signedIn }"
    :while-hover="reducedMotion || busy ? {} : { y: -2, scale: 1.012 }" :transition="spring">
    <motion.span v-if="busy" class="play-progress" :animate="{ scaleX: Math.max(.02, state.game.progress) }" :initial="{ scaleX: 0 }" :transition="{ type: 'spring', stiffness: 120, damping: 24 }"/>
    <motion.button class="play-main" :while-press="press" :transition="spring" :disabled="busy || running || (installed && state.account.busy)" @click="primary">
      <AppIcon v-if="busy" name="LoaderCircle" :size="26" class="spin"/>
      <AppIcon v-else-if="running" name="Gamepad2" :size="28"/>
      <AppIcon v-else-if="!installed" name="Download" :size="26"/>
      <AppIcon v-else-if="!state.account.signedIn" name="LogIn" :size="26"/>
      <svg v-else class="play-glyph" viewBox="0 0 24 24" aria-hidden="true"><path d="M7 4.5v15a1 1 0 0 0 1.5.86l12.4-7.5a1 1 0 0 0 0-1.72L8.5 3.64A1 1 0 0 0 7 4.5Z"/></svg>
      <AnimatePresence mode="wait" :initial="false">
        <motion.span :key="mode" class="play-label" :initial="{ y: 10, opacity: 0 }" :animate="{ y: 0, opacity: 1 }" :exit="{ y: -10, opacity: 0 }" :transition="{ duration: .16 }">{{ label }}</motion.span>
      </AnimatePresence>
    </motion.button>
    <span class="play-split"/>
    <motion.button class="play-more" :while-press="press" :transition="spring" :aria-label="busy && state.game.canCancel ? 'Cancel' : 'More play options'" :aria-expanded="open" @click="secondary">
      <AppIcon v-if="busy && state.game.canCancel" name="X" :size="22"/>
      <motion.span v-else :animate="{ rotate: open ? 180 : 0 }" :transition="spring" style="display:flex"><AppIcon name="ChevronDown" :size="22"/></motion.span>
    </motion.button>
  </motion.div>

  <AnimatePresence>
    <motion.div v-if="open" class="play-menu" role="menu"
      :initial="{ opacity: 0, y: -8, scale: .96 }" :animate="{ opacity: 1, y: 0, scale: 1 }" :exit="{ opacity: 0, y: -6, scale: .97 }" :transition="popSpring">
      <button role="menuitem" :disabled="!state.account.signedIn || running" @click="pick('game.play')"><AppIcon name="Play" :size="16"/>Play {{ state.pack.name }}</button>
      <button v-for="s in state.servers" :key="s.id" role="menuitem" :disabled="!state.account.signedIn || running" @click="pick('game.play', { id: s.id })">
        <AppIcon name="Server" :size="16"/>Join {{ s.name }}<span class="menu-dot" :class="{ online: s.status?.online }"/>
      </button>
      <hr>
      <button v-if="running" role="menuitem" class="danger" @click="pick('game.stop')"><AppIcon name="X" :size="16"/>Force close game</button>
      <button role="menuitem" :disabled="running || busy" @click="pick('game.repair')"><AppIcon name="Wrench" :size="16"/>{{ installed ? 'Repair installation' : 'Install modpack' }}</button>
      <button role="menuitem" @click="pick('folder.open', { id: 'game' })"><AppIcon name="FolderOpen" :size="16"/>Open game folder</button>
    </motion.div>
  </AnimatePresence>
</div>
</template>
