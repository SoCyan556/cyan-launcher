<script setup>
import { motion } from 'motion-v'
import AppIcon from '../components/AppIcon.vue'
import PixelLogo from '../components/PixelLogo.vue'
import PixelScene from '../components/PixelScene.vue'
import PlayButton from '../components/PlayButton.vue'
import { state } from '../state'
import { spring, press, enterRow, reducedMotion } from '../motion'
const emit = defineEmits(['navigate'])
const cards = [
  { page: 'Modpack', icon: 'Leaf', title: 'The Modpack', text: 'Browse the Cyan Civilization mods and choose your optional client-side ones.', scene: 'mountains' },
  { page: 'Settings', icon: 'Settings', title: 'Settings', text: 'Account, memory and game window options.', scene: 'blossom' },
  { page: 'Servers', icon: 'Server', title: 'Servers', text: 'Join the official Comp Craft servers.', scene: 'portal' },
]
const rise = i => ({ initial: { opacity: 0, y: reducedMotion.value ? 0 : 18 }, animate: { opacity: 1, y: 0 }, transition: enterRow(i) })
</script>
<template>
<section class="home">
  <div class="hero">
    <motion.div class="hero-logo" v-bind="rise(0)">
      <motion.div :animate="reducedMotion ? {} : { y: [0, -6, 0] }" :transition="{ duration: 5, repeat: Infinity, ease: 'easeInOut' }"><PixelLogo/></motion.div>
    </motion.div>
    <motion.h1 class="hero-title" v-bind="rise(1)">{{ state.pack.name }}</motion.h1>
    <motion.p class="hero-tagline" v-bind="rise(2)">{{ state.pack.tagline }}</motion.p>
    <motion.div v-bind="rise(3)"><PlayButton/></motion.div>
    <motion.div class="hero-meta" v-bind="rise(4)">
      <span><AppIcon name="Box" :size="16"/>{{ state.pack.packName || state.pack.name }}<template v-if="state.pack.modpackVersion"> v{{ state.pack.modpackVersion }}</template></span>
      <i/>
      <span><AppIcon name="Blocks" :size="16"/>Minecraft {{ state.pack.minecraftVersion }}</span>
    </motion.div>
  </div>
  <div class="feature-cards">
    <motion.button v-for="(c, i) in cards" :key="c.page" class="feature-card"
      :initial="{ opacity: 0, y: reducedMotion ? 0 : 26 }" :animate="{ opacity: 1, y: 0 }" :transition="enterRow(i + 4)"
      :while-hover="reducedMotion ? {} : { y: -5 }" :while-press="press" @click="emit('navigate', c.page)">
      <PixelScene :variant="c.scene" :seed="i + 3" class="card-scene"/>
      <span class="card-body">
        <AppIcon :name="c.icon" :size="30" :stroke="1.5" class="card-icon"/>
        <span class="card-text"><strong>{{ c.title }}</strong><small>{{ c.text }}</small></span>
        <motion.span class="card-arrow" :transition="spring"><AppIcon name="ArrowRight" :size="22"/></motion.span>
      </span>
    </motion.button>
  </div>
</section>
</template>
