<script setup>
import { ref, computed, watch } from 'vue'
import { motion } from 'motion-v'
import AppIcon from '../components/AppIcon.vue'
import PlayerHead from '../components/PlayerHead.vue'
import ToggleSwitch from '../components/ToggleSwitch.vue'
import { state, send, updateSetting } from '../state'
import { spring, press, enterRow, reducedMotion } from '../motion'

const maxMemory = computed(() => Math.max(2048, Math.floor((state.system.memoryMb - 1024) / 512) * 512))
const memory = ref(state.settings.memoryMb)
watch(() => state.settings.memoryMb, v => memory.value = v)
const gb = mb => (mb / 1024).toFixed(1).replace(/\.0$/, '') + ' GB'
const fill = computed(() => ((memory.value - 1024) / (maxMemory.value - 1024)) * 100 + '%')
const memoryHint = computed(() => memory.value < 3072 ? 'Low — may stutter with mods' : memory.value > 8192 ? 'More than this pack needs' : 'Recommended')

const jvm = ref(state.settings.jvmArguments)
watch(() => state.settings.jvmArguments, v => jvm.value = v)
const after = [{ id: 'keep', label: 'Keep open' }, { id: 'minimize', label: 'Minimize' }, { id: 'close', label: 'Close' }]
const folders = [['game', 'Game folder', 'Folder'], ['mods', 'Mods', 'Package'], ['resourcepacks', 'Resource packs', 'Palette'], ['shaderpacks', 'Shader packs', 'Sun'], ['screenshots', 'Screenshots', 'Image'], ['logs', 'Logs', 'Terminal']]
const numberSetting = (key, e) => { const v = parseInt(e.target.value, 10); if (Number.isFinite(v)) updateSetting(key, v) }
const rise = i => ({ initial: { opacity: 0, y: reducedMotion.value ? 0 : 12 }, animate: { opacity: 1, y: 0 }, transition: enterRow(i) })
</script>
<template>
<section class="page">
  <header class="page-head">
    <div class="page-head-icon plain"><AppIcon name="Settings" :size="28"/></div>
    <div><h1>Settings</h1><p>Account, performance and game window options.</p></div>
  </header>
  <div class="scroll settings-grid">
    <div class="settings-col">
      <motion.div class="panel account-panel" v-bind="rise(0)">
        <h2>Account</h2>
        <div class="account-row">
          <PlayerHead :size="44"/>
          <div><strong>{{ state.account.signedIn ? state.account.username : 'Not signed in' }}</strong><small>{{ state.account.signedIn ? 'Microsoft account · Minecraft: Java Edition' : 'You need a Microsoft account that owns Minecraft: Java Edition.' }}</small></div>
          <span class="grow"/>
          <motion.button v-if="state.account.signedIn" class="ghost-button" :while-press="press" :transition="spring" :disabled="state.account.busy" @click="send('auth.signOut')"><AppIcon name="LogOut" :size="15"/>Sign out</motion.button>
          <motion.button v-else class="primary-button" :while-press="press" :transition="spring" :disabled="state.account.busy" @click="send('auth.signIn')"><AppIcon name="LogIn" :size="15"/>{{ state.account.busy ? 'Signing in…' : 'Sign in with Microsoft' }}</motion.button>
        </div>
      </motion.div>

      <motion.div class="panel" v-bind="rise(1)">
        <h2>Memory</h2>
        <div class="slider-head"><span><AppIcon name="MemoryStick" :size="17"/>Allocated to Minecraft</span><output>{{ gb(memory) }}</output></div>
        <input class="slider" type="range" min="1024" :max="maxMemory" step="512" v-model.number="memory" :style="{ '--fill': fill }" aria-label="Memory" @change="updateSetting('memoryMb', memory)"/>
        <div class="slider-scale"><span>1 GB</span><span :class="{ warn: memory < 3072 || memory > 8192 }">{{ memoryHint }}</span><span>{{ gb(maxMemory) }}</span></div>
      </motion.div>

      <motion.div class="panel" v-bind="rise(2)">
        <h2>Game window</h2>
        <div class="field-row">
          <label class="field"><span>Width</span><input type="number" min="640" max="7680" :value="state.settings.width" :disabled="state.settings.fullscreen" @change="numberSetting('width', $event)"/></label>
          <span class="times">×</span>
          <label class="field"><span>Height</span><input type="number" min="360" max="4320" :value="state.settings.height" :disabled="state.settings.fullscreen" @change="numberSetting('height', $event)"/></label>
        </div>
        <ToggleSwitch label="Start in fullscreen" icon="Monitor" :model-value="state.settings.fullscreen" @update:model-value="updateSetting('fullscreen', $event)"/>
      </motion.div>
    </div>

    <div class="settings-col">
      <motion.div class="panel" v-bind="rise(3)">
        <h2>When the game starts</h2>
        <div class="segmented" role="radiogroup" aria-label="Launcher behaviour after launching">
          <motion.button v-for="a in after" :key="a.id" role="radio" :aria-checked="state.settings.afterLaunch === a.id" :while-press="press" :transition="spring" @click="updateSetting('afterLaunch', a.id)">
            <motion.span v-if="state.settings.afterLaunch === a.id" class="segment-pill" layout-id="after-launch" :transition="spring"/>
            <span class="segment-label">{{ a.label }}</span>
          </motion.button>
        </div>
      </motion.div>

      <motion.div class="panel" v-bind="rise(4)">
        <h2>Folders</h2>
        <div class="folder-grid">
          <motion.button v-for="[id, label, icon] in folders" :key="id" class="folder-button" :while-press="press" :transition="spring" @click="send('folder.open', { id })"><AppIcon :name="icon" :size="17"/>{{ label }}</motion.button>
        </div>
      </motion.div>

      <motion.div class="panel" v-bind="rise(5)">
        <h2>Advanced</h2>
        <label class="field"><span>Extra Java arguments</span><input type="text" spellcheck="false" placeholder="e.g. -XX:+UseZGC" v-model="jvm" @change="updateSetting('jvmArguments', jvm)"/></label>
        <ToggleSwitch label="Reduce motion" hint="Turns off launcher animations" icon="Sparkles" :model-value="state.settings.reduceMotion" @update:model-value="updateSetting('reduceMotion', $event)"/>
        <motion.button class="ghost-button danger-button" :while-press="press" :transition="spring" :disabled="!state.modpack.installed || state.game.phase !== 'idle'" @click="send('modpack.resetDefaults')"><AppIcon name="RotateCcw" :size="15"/>Reset game settings to pack defaults…</motion.button>
        <p class="about">{{ state.pack.name }} Launcher v{{ state.launcher.version }}<template v-if="state.modpack.installedVersion"> · {{ state.pack.packName }} v{{ state.modpack.installedVersion }}</template> · Not affiliated with Mojang or Microsoft.</p>
      </motion.div>
    </div>
  </div>
</section>
</template>
