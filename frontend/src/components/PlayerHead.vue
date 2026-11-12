<script setup>
// Generic pixel avatar (no third-party skin service is contacted). Colours are derived from the account name.
import { computed } from 'vue'
import { state } from '../state'
const props = defineProps({ size: { type: Number, default: 20 } })
const palette = computed(() => {
  const name = state.account.username || ''
  let h = 0; for (const c of name) h = (h * 31 + c.charCodeAt(0)) >>> 0
  const hues = [[28, 30], [200, 45], [140, 30], [330, 40], [45, 55]]
  const [hue, sat] = state.account.signedIn ? hues[h % hues.length] : [0, 0]
  return { hair: `hsl(${hue} ${sat}% 22%)`, skin: state.account.signedIn ? '#c8956b' : '#5b5652', shade: state.account.signedIn ? '#a87552' : '#46423f' }
})
</script>
<template>
<svg class="player-head" :width="props.size" :height="props.size" viewBox="0 0 8 8" shape-rendering="crispEdges" aria-hidden="true">
  <rect width="8" height="8" :fill="palette.skin"/>
  <rect width="8" height="2" :fill="palette.hair"/><rect y="2" width="1" height="2" :fill="palette.hair"/><rect x="7" y="2" width="1" height="2" :fill="palette.hair"/>
  <rect x="1" y="4" width="2" height="1" fill="#fff"/><rect x="2" y="4" width="1" height="1" fill="#3b2a6b"/>
  <rect x="5" y="4" width="2" height="1" fill="#fff"/><rect x="5" y="4" width="1" height="1" fill="#3b2a6b"/>
  <rect x="3" y="5" width="2" height="1" :fill="palette.shade"/><rect x="2" y="6" width="4" height="1" fill="#6b4130"/>
</svg>
</template>
