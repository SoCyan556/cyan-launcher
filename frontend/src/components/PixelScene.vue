<script setup>
// Procedural pixel-art landscape. Static layers are painted once into offscreen canvases;
// only the small living details (clouds, petals, water, smoke, fireflies, portal) animate.
import { onMounted, onBeforeUnmount, ref, watch } from 'vue'
import { reducedMotion } from '../motion'

const props = defineProps({ variant: { type: String, default: 'hero' }, seed: { type: Number, default: 7 } })
const canvas = ref(null)

const VARIANTS = {
  hero:      { w: 260, h: 180, horizon: 116, sky: ['#1b1838','#272149','#3b2a5f','#5a3270','#7e3a78','#a8467b','#d05a76','#ec7569','#f7935f','#fdb262'], sun: [118, 108, 13], mountains: true, waterfall: true, lake: true, cottage: true, blossom: true, grass: true, petals: 46, fireflies: 14, clouds: 6, stars: 26 },
  mountains: { w: 200, h: 60,  horizon: 60,  sky: ['#2a2350','#4a3170','#7a3e7c','#b4527a','#e57a6b','#f9a565'], sun: [150, 58, 8], mountains: true, pines: true, clouds: 3, stars: 8 },
  blossom:   { w: 200, h: 60,  horizon: 60,  sky: ['#3b2a5f','#6b3874','#a04a7a','#d4657a','#f08a6d','#fbb06a'], sun: [40, 60, 9], mountains: true, blossom: true, petals: 18, clouds: 2 },
  portal:    { w: 200, h: 60,  horizon: 60,  sky: ['#120d1c','#1a1226','#22172f','#2b1c38','#331f3c'], portal: true, stars: 18, embers: 10 },
}

function rng(seed) { return () => { seed |= 0; seed = seed + 0x6D2B79F5 | 0; let t = Math.imul(seed ^ seed >>> 15, 1 | seed); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296 } }
function layer(w, h) { const c = document.createElement('canvas'); c.width = w; c.height = h; return [c, c.getContext('2d')] }
const px = (g, x, y, w, h, color) => { g.fillStyle = color; g.fillRect(x | 0, y | 0, w, h) }

function paintSky(g, v) {
  const band = v.horizon / v.sky.length
  v.sky.forEach((color, i) => {
    const y0 = Math.floor(i * band), y1 = Math.floor((i + 1) * band)
    px(g, 0, y0, v.w, y1 - y0, color)
    // Checkerboard dither on the seam keeps gradients looking hand-pixelled.
    if (i > 0) { g.fillStyle = v.sky[i - 1]; for (let x = (y0 & 1); x < v.w; x += 2) g.fillRect(x, y0, 1, 1) }
  })
  if (v.horizon < v.h) px(g, 0, v.horizon, v.w, v.h - v.horizon, v.sky.at(-1))
  if (v.sun) {
    const [sx, sy, r] = v.sun
    for (const [rr, color] of [[r + 7, 'rgba(255,190,110,.18)'], [r + 4, 'rgba(255,200,120,.28)']]) disc(g, sx, sy, rr, color)
    disc(g, sx, sy, r, '#ffc979'); disc(g, sx, sy, r - 3, '#ffe2a3'); disc(g, sx - 2, sy - 2, r - 8, '#fff3cf')
  }
}
function disc(g, cx, cy, r, color) { g.fillStyle = color; for (let y = -r; y <= r; y++) { const half = Math.round(Math.sqrt(r * r - y * y)); g.fillRect(cx - half, cy + y, half * 2, 1) } }

function ridge(v, rand, base, amp, peaks) {
  const heights = [], phase = rand() * 10
  for (let x = 0; x < v.w; x++) {
    let y = base + Math.sin(x * .045 + phase) * amp * .35 + Math.sin(x * .13 + phase * 2) * amp * .12
    for (const [px0, height, width] of peaks) y -= height * Math.max(0, 1 - Math.abs(x - px0) / width)
    heights.push(Math.round(y / 2) * 2) // 2px steps read as blocks
  }
  return heights
}

const hash = (x, y, s = 0) => { let h = (x * 374761393 + y * 668265263 + s * 2246822519) | 0; h = Math.imul(h ^ (h >>> 13), 1274126177); return ((h ^ (h >>> 16)) >>> 0) / 4294967296 }

function paintMountains(g, v, rand) {
  const s = v.h / 180, sunX = v.sun?.[0] ?? v.w / 2
  const far = ridge(v, rand, v.horizon - 18 * s, 18 * s, v.variant === 'hero' ? [[208, 74, 62], [165, 30, 34], [40, 18, 40]] : [[150, 22, 40], [60, 14, 40]])
  far.forEach((top, x) => {
    // Faces pointing toward the sun are lit; the far side of each peak falls into shadow.
    const rising = top < (far[x - 1] ?? top) || (top === (far[x - 1] ?? top) && top < (far[x + 1] ?? top))
    const lit = x > sunX ? rising : !rising
    const snowLine = v.horizon - 44 * s + Math.round(hash(x >> 2, 1) * 6)
    for (let y = top; y < v.horizon; y++) {
      const depth = y - top, n = hash(x, y, 3)
      let color
      if (y < snowLine && depth < 9 + hash(x >> 1, 2) * 6) color = depth < 1 ? '#ffffff' : lit ? (n < .15 ? '#eadff0' : '#fbf3f4') : (n < .2 ? '#a996bf' : '#bfaed3')
      else color = depth < 1 ? (lit ? '#8a6c98' : '#5f4a7a') : lit ? (n < .12 ? '#6a5185' : '#5c4679') : (n < .12 ? '#3d2d57' : '#463463')
      px(g, x, y, 1, 1, color)
    }
  })
  const near = ridge(v, rand, v.horizon - 4 * s, 7 * s, v.variant === 'hero' ? [[236, 28, 44]] : [])
  near.forEach((top, x) => { for (let y = top; y < v.horizon; y++) px(g, x, y, 1, 1, y - top < 1 ? '#45663f' : hash(x, y, 9) < .12 ? '#22382a' : '#2a4331') })
  return near
}

function paintCliff(g, v, x0, top, width) {
  // Mossy rock outcrop the waterfall pours from.
  for (let y = top; y < v.horizon; y++) {
    const t = (y - top) / (v.horizon - top)
    const left = Math.round(x0 - t * 10 - hash(1, y >> 1) * 3), right = Math.round(x0 + width + t * 12 + hash(2, y >> 1) * 3)
    for (let x = left; x < right; x++) {
      const n = hash(x >> 1, y >> 1, 4)
      const edge = x - left < 2 || right - x < 2
      px(g, x, y, 1, 1, y - top < 3 && n < .8 ? (n < .4 ? '#4f7a3c' : '#3d6331') : edge ? '#2a302c' : n < .25 ? '#3f4a43' : n < .55 ? '#4a5650' : n < .7 ? '#3b5a35' : '#55625b')
    }
  }
}

function tree(g, x, base, size, palette) {
  const [dark, mid, light] = palette
  px(g, x, base - size * .4, 1, size * .4, '#3a2a20')
  for (let i = 0; i < size; i++) {
    const half = Math.max(1, Math.round((size - i) * .45))
    px(g, x - half, base - size * .3 - i, half * 2 + 1, 1, i % 3 === 0 ? dark : mid)
    if (i % 2 === 0) px(g, x - half + 1, base - size * .3 - i, 1, 1, light)
  }
}

function paintCottage(g, x, y) {
  px(g, x - 2, y - 30, 4, 10, '#3d2a22'); px(g, x - 3, y - 31, 6, 2, '#4d372c')                 // chimney
  for (let i = 0; i < 14; i++) px(g, x + 2 + i, y - 26 + i, 36 - i * 2, 1, i % 2 ? '#4a2f26' : '#5a3a2e') // roof (stepped)
  for (let i = 0; i < 14; i++) px(g, x + 2 + i, y - 26 + i, 2, 1, '#2c1c17')
  px(g, x + 4, y - 12, 32, 16, '#7a5233')                                                         // planks
  for (let r = 0; r < 4; r++) px(g, x + 4, y - 12 + r * 4, 32, 1, '#5e3e27')
  px(g, x + 4, y - 12, 2, 16, '#4b3020'); px(g, x + 34, y - 12, 2, 16, '#4b3020')
  for (const wx of [x + 9, x + 25]) { px(g, wx - 1, y - 9, 8, 8, '#3a2618'); px(g, wx, y - 8, 6, 6, '#ffb84d'); px(g, wx + 2, y - 8, 1, 6, '#c27a2a'); px(g, wx, y - 6, 6, 1, '#c27a2a') }
  px(g, x + 17, y - 6, 6, 10, '#3d281b'); px(g, x + 21, y - 1, 1, 1, '#ffcf6b')
}

function paintBlossomCanopy(g, v, rand, cx, cy, radius) {
  // A canopy is a loose bunch of overlapping clumps; each clump is shaded dark underneath, light on top.
  const colors = ['#9e4a74', '#c45f8c', '#e07fa8', '#f2a7c3', '#ffd6e6']
  const clumps = Array.from({ length: Math.round(radius / 2.2) }, () => {
    const a = rand() * Math.PI * 2, d = rand() * radius * .9
    return [cx + Math.cos(a) * d * 1.5, cy + Math.sin(a) * d * .7, radius * (.35 + rand() * .3)]
  })
  for (const [x0, y0, r] of clumps) for (let y = -r; y <= r; y += 2) for (let x = -r * 1.3; x <= r * 1.3; x += 2) {
    const k = (x / 1.3) ** 2 + y ** 2, jitter = hash(Math.round(x0 + x) >> 1, Math.round(y0 + y) >> 1, 5)
    if (k > r * r * (.75 + jitter * .4)) continue
    const light = Math.max(0, Math.min(4, Math.round(2.4 - (y / r) * 1.8 + (jitter - .5) * 1.6)))
    px(g, Math.round((x0 + x) / 2) * 2, Math.round((y0 + y) / 2) * 2, 2, 2, colors[light])
  }
}

function paintForeground(g, v, rand) {
  const top = v.h - 26
  for (let x = 0; x < v.w; x++) {
    const h = top + Math.round(Math.sin(x * .07) * 2 + rand() * 2)
    for (let y = h; y < v.h; y++) px(g, x, y, 1, 1, y - h < 1 ? '#5f8f3e' : (x + y * 3) % 9 === 0 ? '#2f4d27' : y - h < 4 ? '#3f6a31' : '#2a4523')
    if (rand() < .35) px(g, x, h - 1 - Math.floor(rand() * 4), 1, 2 + Math.floor(rand() * 3), rand() < .5 ? '#6ea446' : '#86b955')
    if (rand() < .05) { const c = ['#f2a7c3', '#c879e0', '#ffd36b', '#ffffff'][Math.floor(rand() * 4)]; const fy = h - 3 - Math.floor(rand() * 4); px(g, x, fy + 1, 1, 3, '#4f8236'); px(g, x - 1, fy, 3, 1, c); px(g, x, fy - 1, 1, 3, c) }
  }
}

function build(v) {
  const rand = rng(props.seed * 977 + v.w)
  const [back, bg] = layer(v.w, v.h), [front, fg] = layer(v.w, v.h)
  paintSky(bg, v)
  if (v.mountains) {
    paintMountains(fg, v, rand)
    if (v.pines) for (let x = 4; x < v.w; x += 5 + Math.floor(rand() * 7)) tree(fg, x, v.horizon + 1, 6 + Math.floor(rand() * 7), ['#1d3324', '#26422d', '#36583a'])
  }
  if (v.waterfall) { paintCliff(fg, v, 203, 50, 18); v.fall = [210, 52, 6, v.horizon - 52] }
  if (v.variant === 'hero') for (let x = 132; x < v.w; x += 6 + Math.floor(rand() * 8)) if (x < 190 || x > 232) tree(fg, x, v.horizon - 1, 7 + Math.floor(rand() * 8), ['#1f3828', '#2a4a32', '#3d6a41'])
  if (v.lake) {
    // Mirror everything above the horizon into the lake, darkened.
    const [mirror, mg] = layer(v.w, v.h)
    mg.drawImage(back, 0, 0); mg.drawImage(front, 0, 0)
    const depth = v.h - 26 - v.horizon
    fg.save(); fg.translate(0, v.horizon * 2); fg.scale(1, -1); fg.drawImage(mirror, 0, v.horizon - depth, v.w, depth, 0, v.horizon - depth, v.w, depth); fg.restore()
    fg.fillStyle = 'rgba(28,18,48,.42)'; fg.fillRect(0, v.horizon, v.w, depth)
    px(fg, 0, v.horizon, v.w, 1, 'rgba(255,214,160,.18)')
  }
  if (v.cottage) { for (let x = 0; x < 96; x++) { const y = v.horizon - 3 + Math.round(Math.sin(x * .09) * 2); px(fg, x, y, 1, v.h - y, '#2c4528') } paintCottage(fg, 30, v.horizon - 2); tree(fg, 84, v.horizon - 2, 16, ['#1f3828', '#2a4a32', '#3d6a41']); tree(fg, 8, v.horizon - 2, 13, ['#1f3828', '#2a4a32', '#3d6a41']) }
  if (v.grass) paintForeground(fg, v, rand)
  if (v.blossom) {
    if (v.variant === 'hero') {
      // Thick trunk rising from the left edge, with two branches reaching into the canopy.
      for (let y = 14; y < v.h; y++) { const x = Math.round(-2 + Math.max(0, 40 - y) * .12); px(fg, x, y, 8, 1, '#3a2620'); px(fg, x + 5, y, 2, 1, '#57392a'); if (hash(1, y >> 1) < .3) px(fg, x + 2, y, 1, 1, '#2a1a15') }
      for (let i = 0; i < 34; i++) { px(fg, 4 + i, 26 - i * .55, 3, 2, '#3a2620'); px(fg, 4 + i, 26 - i * .55, 3, 1, '#57392a') }
      for (let i = 0; i < 18; i++) px(fg, 3 + i * .4, 30 - i, 2, 1, '#3a2620')
      paintBlossomCanopy(fg, v, rand, 6, 6, 22); paintBlossomCanopy(fg, v, rand, 44, -2, 14); paintBlossomCanopy(fg, v, rand, -2, 34, 12); paintBlossomCanopy(fg, v, rand, 258, -4, 12) }
    else { for (const [cx, cy, r] of [[30, 34, 10], [96, 40, 9], [160, 30, 11]]) { px(fg, cx, cy + 4, 2, v.h - cy, '#3a2620'); paintBlossomCanopy(fg, v, rand, cx, cy, r) } }
  }
  if (v.portal) {
    for (let x = 0; x < v.w; x++) for (let y = 48; y < v.h; y++) px(fg, x, y, 1, 1, (x * 7 + y * 3) % 5 === 0 ? '#4a1f22' : (x + y) % 3 === 0 ? '#6b2a2a' : '#5a2426')
    v.frame = [82, 6, 36, 44]
    const [x0, y0, w, h] = v.frame
    for (let x = x0; x < x0 + w; x += 6) for (let y = y0; y < y0 + h; y += 6) if (x < x0 + 6 || x >= x0 + w - 6 || y < y0 + 6 || y >= y0 + h - 6) { px(fg, x, y, 6, 6, '#1a1024'); px(fg, x + 1, y + 1, 2, 1, '#3d2a5a') }
  }
  return { back, front }
}

let raf = 0, last = 0, layers = null, v = null, particles = []
function seedParticles() {
  const rand = rng(props.seed * 31)
  particles = []
  for (let i = 0; i < (v.clouds || 0); i++) particles.push({ kind: 'cloud', x: rand() * v.w, y: 6 + rand() * v.horizon * .45, w: 14 + rand() * 26, speed: 1.2 + rand() * 1.6 })
  for (let i = 0; i < (v.stars || 0); i++) particles.push({ kind: 'star', x: rand() * v.w, y: rand() * v.horizon * .4, phase: rand() * 6 })
  for (let i = 0; i < (v.petals || 0); i++) particles.push({ kind: 'petal', x: rand() * v.w, y: rand() * v.h, speed: 6 + rand() * 9, sway: rand() * 6, color: rand() < .5 ? '#f6b6cf' : '#e88db4' })
  for (let i = 0; i < (v.fireflies || 0); i++) particles.push({ kind: 'firefly', x: rand() * v.w, y: v.h - 40 + rand() * 22, phase: rand() * 6 })
  for (let i = 0; i < (v.embers || 0); i++) particles.push({ kind: 'ember', x: rand() * v.w, y: rand() * v.h, speed: 3 + rand() * 5, phase: rand() * 6 })
}

function frame(time) {
  const g = canvas.value?.getContext('2d')
  if (!g || !layers) return
  const t = time / 1000, dt = last ? Math.min(.1, t - last) : 0
  last = t
  g.drawImage(layers.back, 0, 0)
  for (const p of particles) if (p.kind === 'star' && (Math.sin(t * 2 + p.phase) > -.3)) px(g, p.x, p.y, 1, 1, Math.sin(t * 2 + p.phase) > .7 ? '#ffffff' : '#c9b8e8')
  for (const p of particles) if (p.kind === 'cloud') {
    p.x += p.speed * dt; if (p.x > v.w + 10) p.x = -p.w - 10
    const x = Math.round(p.x), y = Math.round(p.y), w = Math.round(p.w)
    px(g, x + 4, y - 3, w - 10, 3, '#ffc9bd'); px(g, x, y, w, 4, '#f4a3a8'); px(g, x + 3, y + 4, w - 4, 2, '#c8708f')
  }
  g.drawImage(layers.front, 0, 0)
  if (v.fall) {
    const [fx, fy, fw, fh] = v.fall
    px(g, fx, fy, fw, fh, '#6fb4ec')
    for (let y = 0; y < fh; y += 4) px(g, fx + ((y >> 2) % 3), fy + ((y + Math.floor(t * 40)) % fh), 2, 2, '#d6f0ff')
    px(g, fx - 3, fy + fh - 2, fw + 6, 2, (Math.floor(t * 8) % 2) ? '#e8f6ff' : '#bfe3ff')
  }
  if (v.lake) for (let i = 0; i < 18; i++) {
    const y = v.horizon + 3 + ((i * 7) % (v.h - 30 - v.horizon)), x = (i * 53 + Math.floor(t * (6 + i % 4) * 3)) % v.w
    px(g, x, y, 3 + i % 4, 1, i % 3 ? 'rgba(255,200,150,.35)' : 'rgba(255,240,220,.5)')
  }
  if (v.sun && v.lake) for (let y = v.horizon + 2; y < v.h - 28; y += 2) {
    // Broken, flickering glints rather than a solid column.
    const spread = 4 + (y - v.horizon) * .5, tick = Math.floor(t * 5)
    for (let k = 0; k < 3; k++) if (hash(y, k, tick) < .55) {
      const x = v.sun[0] + Math.round((hash(k, y, tick + 1) - .5) * spread * 2)
      px(g, x, y, 2 + Math.round(hash(y, k) * 4), 1, hash(k, y) < .5 ? 'rgba(255,224,160,.6)' : 'rgba(255,190,120,.4)')
    }
  }
  if (v.cottage) for (let i = 0; i < 6; i++) { const age = (t * .35 + i / 6) % 1; px(g, 30 - 1 + Math.sin(age * 6 + i) * 3 + age * 8, v.horizon - 34 - age * 34, 2 + Math.round(age * 2), 2 + Math.round(age * 2), `rgba(200,190,210,${.55 * (1 - age)})`) }
  if (v.portal) {
    const [x0, y0, w, h] = v.frame
    for (let y = y0 + 6; y < y0 + h - 6; y += 2) for (let x = x0 + 6; x < x0 + w - 6; x += 2) {
      const s = Math.sin(x * .35 + t * 2.4) + Math.sin(y * .3 - t * 3.1) + Math.sin((x + y) * .2 + t)
      px(g, x, y, 2, 2, s > 1.2 ? '#e3a6ff' : s > 0 ? '#b052f0' : s > -1.2 ? '#7a2fc8' : '#561f99')
    }
    g.fillStyle = 'rgba(160,80,240,.10)'; g.fillRect(x0 - 10, y0 - 6, w + 20, h + 12)
  }
  for (const p of particles) {
    if (p.kind === 'petal') {
      p.y += p.speed * dt; p.x += (4 + Math.sin(t + p.sway) * 6) * dt
      if (p.y > v.h || p.x > v.w) { p.y = -2; p.x = Math.random() * v.w * .7 }
      px(g, p.x, p.y, (Math.floor(t * 4 + p.sway) % 2) + 1, 1, p.color)
    } else if (p.kind === 'firefly') {
      const glow = (Math.sin(t * 1.7 + p.phase) + 1) / 2
      if (glow > .45) { px(g, p.x + Math.sin(t + p.phase) * 4, p.y + Math.cos(t * .7 + p.phase) * 3, 1, 1, `rgba(255,236,140,${glow})`) }
    } else if (p.kind === 'ember') {
      p.y -= p.speed * dt; if (p.y < 0) { p.y = v.h; p.x = Math.random() * v.w }
      px(g, p.x + Math.sin(t + p.phase) * 3, p.y, 1, 1, Math.sin(t * 5 + p.phase) > 0 ? '#ff9a4a' : '#c85a2a')
    }
  }
}

function loop(time) { frame(time); raf = (reducedMotion.value || document.hidden) ? 0 : requestAnimationFrame(loop) }
function resume() { if (!raf && !reducedMotion.value && !document.hidden) raf = requestAnimationFrame(loop) }
function setup() {
  v = { ...VARIANTS[props.variant] ?? VARIANTS.hero, variant: props.variant }
  canvas.value.width = v.w; canvas.value.height = v.h
  layers = build(v); seedParticles(); last = 0
  frame(performance.now()); resume()
}
onMounted(() => { setup(); document.addEventListener('visibilitychange', resume) })
onBeforeUnmount(() => { cancelAnimationFrame(raf); document.removeEventListener('visibilitychange', resume) })
watch(reducedMotion, on => { if (on) { cancelAnimationFrame(raf); raf = 0 } else resume() })
watch(() => props.variant, setup)
</script>
<template><canvas ref="canvas" class="pixel-scene" aria-hidden="true"/></template>
