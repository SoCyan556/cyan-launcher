<script setup>
import { ref } from 'vue'
import { motion, AnimatePresence } from 'motion-v'
import AppIcon from '../components/AppIcon.vue'
import { state, send, busyPhases } from '../state'
import { spring, press, enterRow, reducedMotion, popSpring } from '../motion'
const copied = ref(null)
function copy(s) {
  const text = s.port === 25565 ? s.address : `${s.address}:${s.port}`
  navigator.clipboard?.writeText(text).then(() => { copied.value = s.id; setTimeout(() => copied.value = null, 1400) }).catch(() => {})
}
const status = s => state.serversBusy && !s.status ? 'checking' : s.status?.online ? 'online' : s.status ? 'offline' : 'checking'
const quality = ms => ms < 80 ? 4 : ms < 150 ? 3 : ms < 250 ? 2 : 1
</script>
<template>
<section class="page">
  <header class="page-head">
    <div class="page-head-icon plain"><AppIcon name="Server" :size="28"/></div>
    <div><h1>Servers</h1><p>Join the official {{ state.pack.name }} servers. Joining launches the game straight into the server.</p></div>
    <span class="grow"/>
    <motion.button class="ghost-button" :while-press="press" :transition="spring" :disabled="state.serversBusy" @click="send('servers.refresh')"><AppIcon name="RefreshCw" :size="15" :class="{ spin: state.serversBusy }"/>Refresh</motion.button>
  </header>
  <div class="scroll">
    <p v-if="!state.servers.length" class="empty">No servers are listed yet.</p>
    <div class="server-list">
      <motion.article v-for="(s, i) in state.servers" :key="s.id" class="server-card" :class="status(s)"
        :initial="{ opacity: 0, y: reducedMotion ? 0 : 14 }" :animate="{ opacity: 1, y: 0 }" :transition="enterRow(i)">
        <img v-if="s.status?.favicon" :src="s.status.favicon" alt="" class="server-icon"/>
        <span v-else class="server-icon fallback"><AppIcon name="Server" :size="26"/></span>
        <div class="server-text">
          <div class="server-title"><strong>{{ s.name }}</strong><span class="status-pill"><i/>{{ { online: 'Online', offline: 'Offline', checking: 'Checking…' }[status(s)] }}</span></div>
          <small class="server-motd">{{ s.status?.motd || s.description }}</small>
          <button class="server-address" :title="'Copy address'" @click="copy(s)">
            <AppIcon :name="copied === s.id ? 'Check' : 'Copy'" :size="13"/>{{ s.address }}<template v-if="s.port !== 25565">:{{ s.port }}</template>
            <AnimatePresence><motion.em v-if="copied === s.id" :initial="{ opacity: 0, x: -4 }" :animate="{ opacity: 1, x: 0 }" :exit="{ opacity: 0 }" :transition="popSpring">Copied</motion.em></AnimatePresence>
          </button>
        </div>
        <div class="server-stats" v-if="s.status?.online">
          <span><AppIcon name="Users" :size="15"/>{{ s.status.players ?? '?' }} / {{ s.status.maxPlayers ?? '?' }}</span>
          <span class="signal" :title="s.status.latency + ' ms'"><i v-for="b in 4" :key="b" :class="{ on: b <= quality(s.status.latency) }"/>{{ s.status.latency }} ms</span>
        </div>
        <motion.button class="join-button" :while-press="press" :while-hover="reducedMotion ? {} : { y: -1 }" :transition="spring"
          :disabled="!state.account.signedIn || busyPhases.includes(state.game.phase) || state.game.phase === 'running'" @click="send('game.play', { id: s.id })">
          <AppIcon name="Play" :size="16"/>Join
        </motion.button>
      </motion.article>
    </div>
    <p v-if="!state.account.signedIn" class="hint"><AppIcon name="Info" :size="14"/>Sign in with Microsoft to join servers.</p>
  </div>
</section>
</template>
