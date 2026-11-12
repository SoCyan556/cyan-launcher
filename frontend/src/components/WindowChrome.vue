<script setup>
// Frameless window chrome: drag strip (native via WebView2 app-region), caption buttons, account chip and edge resize handles.
import { motion } from 'motion-v'
import AppIcon from './AppIcon.vue'
import PlayerHead from './PlayerHead.vue'
import { state, send } from '../state'
import { spring, press } from '../motion'
const emit = defineEmits(['account'])
const edges = ['t', 'b', 'l', 'r', 'tl', 'tr', 'bl', 'br']
</script>
<template>
<div class="drag-strip" aria-hidden="true"/>
<div class="caption">
  <motion.button class="account-chip" :while-press="press" :transition="spring" :title="state.account.signedIn ? 'Account settings' : 'Sign in with Microsoft'"
    @click="state.account.signedIn ? emit('account') : send('auth.signIn')" :disabled="state.account.busy">
    <PlayerHead :size="20"/>
    <span>{{ state.account.busy ? 'Signing in…' : state.account.signedIn ? state.account.username : 'Sign in' }}</span>
  </motion.button>
  <span class="caption-divider"/>
  <button class="caption-button" aria-label="Minimize" @click="send('window.minimize')"><AppIcon name="Minus" :size="17"/></button>
  <button class="caption-button" :aria-label="state.window.maximized ? 'Restore' : 'Maximize'" @click="send('window.maximize')"><AppIcon :name="state.window.maximized ? 'Copy' : 'Square'" :size="state.window.maximized ? 14 : 13"/></button>
  <button class="caption-button close" aria-label="Close" @click="send('window.close')"><AppIcon name="X" :size="18"/></button>
</div>
<template v-if="state.host && !state.window.maximized">
  <div v-for="e in edges" :key="e" :class="['resize-edge', 'edge-' + e]" @mousedown.left.prevent="send('window.resize', { edge: e })"/>
</template>
</template>
