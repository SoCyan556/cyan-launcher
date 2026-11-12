<script setup>
// Isometric grass block icon: an 8×8 pixel texture mapped onto the three visible faces.
const n = 8
const greens = ['#6fb43f', '#5ea235', '#83c64d', '#4f8f2d'], dirt = ['#8a5a36', '#7a4e2e', '#9a6a40', '#6b4428']
const faces = [
  { o: [16, 0], a: [1, .5], b: [-1, .5], color: (i, j, h) => greens[h], light: 1 },
  { o: [8, 4], a: [1, .5], b: [0, 1], color: (i, j, h) => (j < 2 || (j === 2 && i % 3 === 0)) ? greens[(h + 1) % 4] : dirt[h], light: .82 },
  { o: [16, 8], a: [1, -.5], b: [0, 1], color: (i, j, h) => (j < 2 || (j === 2 && i % 3 === 1)) ? greens[(h + 2) % 4] : dirt[(h + 1) % 4], light: .64 },
]
const pt = (f, u, v) => `${f.o[0] + u * f.a[0] + v * f.b[0]},${f.o[1] + u * f.a[1] + v * f.b[1]}`
const cells = faces.flatMap((f, fi) => Array.from({ length: n * n }, (_, k) => {
  const i = k % n, j = Math.floor(k / n), h = (i * 7 + j * 13 + fi) % 4
  return { key: `${fi}-${k}`, points: [pt(f, i, j), pt(f, i + 1, j), pt(f, i + 1, j + 1), pt(f, i, j + 1)].join(' '), fill: f.color(i, j, h), opacity: f.light }
}))
</script>
<template>
<svg class="grass-block" viewBox="7.5 -.5 17 17" aria-hidden="true">
  <polygon v-for="c in cells" :key="c.key" :points="c.points" :fill="c.fill" :fill-opacity="c.opacity" stroke="none"/>
  <polygon points="16,0 24,4 24,12 16,16 8,12 8,4" fill="none" stroke="#1a120c" stroke-width=".35" stroke-linejoin="round"/>
</svg>
</template>
