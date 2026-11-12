<script setup>
import { computed, ref } from 'vue'
import { motion } from 'motion-v'
import AppIcon from '../components/AppIcon.vue'
import GrassBlock from '../components/GrassBlock.vue'
import { state, send, busyPhases } from '../state'
import { spring, press, enterRow, reducedMotion } from '../motion'

const optional = computed(() => state.modpack.mods.filter(m => m.clientSide))
const included = computed(() => state.modpack.mods.filter(m => !m.clientSide))
const query = ref('')
const shown = computed(() => {
  const q = query.value.trim().toLowerCase()
  return q ? included.value.filter(m => m.title.toLowerCase().includes(q) || (m.description || '').toLowerCase().includes(q)) : included.value
})
const busy = computed(() => busyPhases.includes(state.game.phase) || state.game.phase === 'running')
const statusText = computed(() => {
  const m = state.modpack
  if (m.status === 'checking') return 'Checking GitHub for modpack updates…'
  if (m.status === 'unavailable') return 'Could not reach the update server. The mod list loads when you are back online.'
  const offline = m.status === 'offline' ? 'Offline — showing the last downloaded mod list. ' : ''
  if (!m.installed) return offline + 'Not installed yet. Press Install modpack on the Home page to download everything in one go.'
  if (m.updateAvailable) return offline + `Update available: ${m.installedVersion} → ${state.pack.modpackVersion}. It installs when you press Update & Play.`
  return offline + `Installed and up to date (v${m.installedVersion}).`
})
function toggle(m) { if (!busy.value) send('modpack.toggle', { path: m.path, enabled: !m.enabled }) }
function open(m) { if (m.projectUrl) send('link.open', { url: m.projectUrl }) }
const rise = i => ({ initial: { opacity: 0, y: reducedMotion.value ? 0 : 12 }, animate: { opacity: 1, y: 0 }, transition: enterRow(i) })
</script>
<template>
<section class="page">
  <header class="page-head">
    <div class="page-head-icon"><GrassBlock/></div>
    <div><h1>{{ state.pack.packName || 'The Modpack' }}</h1><p>{{ state.pack.description }}</p></div>
  </header>
  <div class="chips">
    <span class="chip"><AppIcon name="Blocks" :size="15"/>Minecraft {{ state.pack.minecraftVersion }}</span>
    <span class="chip"><AppIcon name="Layers" :size="15"/>{{ state.pack.loader }}</span>
    <span class="chip"><AppIcon name="Package" :size="15"/>{{ state.modpack.mods.length }} mods<template v-if="optional.length"> · {{ optional.length }} optional</template></span>
    <span v-if="state.pack.modpackVersion" class="chip accent"><AppIcon name="Sparkles" :size="15"/>v{{ state.pack.modpackVersion }}</span>
    <span class="grow"/>
    <motion.button class="ghost-button" :while-press="press" :transition="spring" :disabled="state.modpack.status === 'checking'" @click="send('modpack.refresh')"><AppIcon name="RefreshCw" :size="15" :class="{ spin: state.modpack.status === 'checking' }"/>Check for updates</motion.button>
    <motion.button class="ghost-button" :while-press="press" :transition="spring" @click="send('folder.open', { id: 'mods' })"><AppIcon name="FolderOpen" :size="15"/>Mods folder</motion.button>
  </div>
  <p class="status-line" :class="state.modpack.status">{{ statusText }}</p>

  <div class="scroll">
    <template v-if="optional.length">
      <h2 class="section-title">Optional client-side mods</h2>
      <p class="section-hint">Turn these on or off for your own game. Changes apply the next time you press Play.</p>
      <div class="mod-grid">
        <motion.article v-for="(m, i) in optional" :key="m.path" class="mod-card" :class="{ off: !m.enabled }" v-bind="rise(i)">
          <img v-if="m.iconUrl" :src="m.iconUrl" alt="" loading="lazy"/><span v-else class="mod-fallback"><AppIcon name="Package" :size="22"/></span>
          <div class="mod-text">
            <strong>{{ m.title }}</strong>
            <small>{{ m.description || m.version || '' }}</small>
          </div>
          <button class="icon-button mod-link" v-if="m.projectUrl" :aria-label="'Open ' + m.title + ' page'" @click="open(m)"><AppIcon name="ExternalLink" :size="15"/></button>
          <motion.button type="button" class="mod-switch" role="switch" :aria-checked="m.enabled" :aria-label="(m.enabled ? 'Turn off ' : 'Turn on ') + m.title"
            :disabled="busy || !m.installed" :while-press="press" :transition="spring" @click="toggle(m)">
            <span class="switch-track" :class="{ on: m.enabled }"><motion.span :animate="{ x: m.enabled ? 16 : 0 }" :initial="false" :transition="reducedMotion ? { duration: 0 } : { type: 'spring', stiffness: 520, damping: 23 }"/></span>
          </motion.button>
        </motion.article>
      </div>
    </template>

    <div class="section-row">
      <h2 class="section-title">{{ optional.length ? 'Included mods' : 'Mods' }}</h2>
      <label class="mod-search"><AppIcon name="Search" :size="15"/><input v-model="query" type="search" placeholder="Search mods" spellcheck="false" aria-label="Search mods"/></label>
    </div>
    <div class="mod-grid">
      <motion.article v-for="(m, i) in shown" :key="m.path" class="mod-card" v-bind="rise(i)">
        <img v-if="m.iconUrl" :src="m.iconUrl" alt="" loading="lazy"/><span v-else class="mod-fallback"><AppIcon name="Package" :size="22"/></span>
        <div class="mod-text">
          <strong>{{ m.title }}</strong>
          <small>{{ m.description || (m.version ? 'Version ' + m.version : '') }}</small>
        </div>
        <span class="mod-state" :class="{ ok: m.installed }" :title="m.installed ? 'Installed' : 'Downloads when you install the modpack'">
          <AppIcon :name="m.installed ? 'Check' : 'Download'" :size="15"/>
        </span>
        <button class="icon-button mod-link" v-if="m.projectUrl" :aria-label="'Open ' + m.title + ' page'" @click="open(m)"><AppIcon name="ExternalLink" :size="15"/></button>
      </motion.article>
    </div>
    <p v-if="!shown.length && state.modpack.mods.length" class="status-line">No mods match “{{ query }}”.</p>
  </div>
</section>
</template>
