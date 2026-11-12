<script setup>
import { ref, onMounted, watchEffect } from 'vue'
import { motion, MotionConfig } from 'motion-v'
import AppIcon from './components/AppIcon.vue'
import PixelLogo from './components/PixelLogo.vue'
import PixelScene from './components/PixelScene.vue'
import GrassBlock from './components/GrassBlock.vue'
import WindowChrome from './components/WindowChrome.vue'
import StatusToast from './components/StatusToast.vue'
import HomePage from './pages/HomePage.vue'
import ModpackPage from './pages/ModpackPage.vue'
import ServersPage from './pages/ServersPage.vue'
import SettingsPage from './pages/SettingsPage.vue'
import { state, start } from './state'
import { spring, press, reducedMotion } from './motion'

const nav = [
  { label: 'Home', icon: 'House', page: HomePage },
  { label: 'Modpack', icon: 'Layers', page: ModpackPage },
  { label: 'Servers', icon: 'Server', page: ServersPage },
  { label: 'Settings', icon: 'Settings', page: SettingsPage },
]
const page = ref('Home')
const pages = Object.fromEntries(nav.map(n => [n.label, n.page]))
function navigate(name) { page.value = name }
onMounted(start)
watchEffect(() => document.documentElement.classList.toggle('reduce-motion', !!state.settings.reduceMotion))
</script>

<template>
<MotionConfig :reduced-motion="state.settings.reduceMotion ? 'always' : 'user'">
<div class="app">
  <aside class="sidebar">
    <div class="brand">
      <motion.div class="brand-logo" :while-hover="reducedMotion ? {} : { rotate: -3, scale: 1.05 }" :transition="spring"><PixelLogo/></motion.div>
      <span class="brand-name">{{ state.pack.name }}</span>
    </div>
    <nav aria-label="Main navigation">
      <motion.button v-for="n in nav" :key="n.label" :class="{ active: page === n.label }" :aria-current="page === n.label ? 'page' : undefined"
        :while-press="press" :transition="spring" @click="navigate(n.label)">
        <motion.span v-if="page === n.label" class="nav-pill" layout-id="nav-pill" :transition="spring" aria-hidden="true"/>
        <AppIcon :name="n.icon" :size="22"/><span class="nav-label">{{ n.label }}</span>
      </motion.button>
    </nav>
    <motion.button class="pack-card" :while-press="press" :while-hover="reducedMotion ? {} : { y: -2 }" :transition="spring" @click="navigate('Modpack')">
      <GrassBlock/>
      <span><strong>{{ state.pack.packName || state.pack.name }}</strong><small v-if="state.pack.modpackVersion">v{{ state.pack.modpackVersion }}</small></span>
    </motion.button>
  </aside>

  <main class="stage">
    <PixelScene variant="hero" class="stage-scene"/>
    <div class="stage-vignette"/>
    <motion.div class="stage-dim" :initial="false" :animate="{ opacity: page === 'Home' ? 0 : 1 }" :transition="{ duration: reducedMotion ? 0 : .35 }"/>
    <WindowChrome @account="navigate('Settings')"/>
    <StatusToast/>
    <Transition name="page" mode="out-in">
      <component :is="pages[page]" :key="page" @navigate="navigate"/>
    </Transition>
  </main>
</div>
</MotionConfig>
</template>
