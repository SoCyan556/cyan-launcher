<script setup>
// Chunky extruded "CC" mark: a pale stone C and an amber C, each a solid block letter with depth,
// edge highlights and a few leaf blocks growing over the corners.
const glyph = ['.#####.', '###..##', '##.....', '##.....', '###..##', '.#####.']
const cells = []
glyph.forEach((row, y) => [...row].forEach((c, x) => { if (c === '#') cells.push([x, y]) }))
const filled = new Set(cells.map(([x, y]) => `${x},${y}`))
const has = (x, y) => filled.has(`${x},${y}`)
const depth = [.42, .5]

const letters = [
  { dx: 0, dy: 0, face: '#ece0cb', top: '#fffaf0', edge: '#c9b79c', side: '#9c8a70', fleck: '#d8c7ab', crack: '#bfae93' },
  { dx: 7.3, dy: .3, face: '#f39a1e', top: '#ffcb6b', edge: '#d47a12', side: '#a5560a', fleck: '#e7890f', crack: '#c86f0c' },
].map(l => ({
  ...l,
  topEdges: cells.filter(([x, y]) => !has(x, y - 1)),
  bottomEdges: cells.filter(([x, y]) => !has(x, y + 1)),
  leftEdges: cells.filter(([x, y]) => !has(x - 1, y)),
  rightEdges: cells.filter(([x, y]) => !has(x + 1, y)),
}))
const fleck = (x, y, i) => (x * 7 + y * 13 + i * 5) % 5 === 0
const leaves = [[-.6, -.55, '#6fae3c'], [.35, -.85, '#4d8a2c'], [-.85, .4, '#9cd35a'], [1.3, -.5, '#83c64d'],
  [13.3, -.25, '#4d8a2c'], [13.9, .55, '#6fae3c'], [12.6, -.7, '#9cd35a'], [13.85, 5.7, '#4d8a2c'], [13.1, 6.45, '#6fae3c'], [-.55, 5.55, '#4d8a2c'], [.25, 6.25, '#83c64d']]
</script>
<template>
<svg class="pixel-logo" viewBox="-1.4 -1.4 16.4 9.6" role="img" aria-label="Comp Craft" shape-rendering="crispEdges">
  <g v-for="(l, li) in letters" :key="li" :transform="`translate(${l.dx} ${l.dy})`">
    <!-- outline + extrusion -->
    <rect v-for="([x, y], i) in cells" :key="'o' + i" :x="x - .18" :y="y - .18" :width="1.36 + depth[0]" :height="1.36 + depth[1]" fill="#24170f"/>
    <rect v-for="([x, y], i) in cells" :key="'s' + i" :x="x + depth[0]" :y="y + depth[1]" width="1" height="1" :fill="l.side"/>
    <!-- face -->
    <rect v-for="([x, y], i) in cells" :key="'f' + i" :x="x" :y="y" width="1.01" height="1.01" :fill="l.face"/>
    <rect v-for="([x, y], i) in cells" v-show="fleck(x, y, li)" :key="'k' + i" :x="x + .3" :y="y + .45" width=".3" height=".15" :fill="l.crack"/>
    <rect v-for="([x, y], i) in cells" v-show="fleck(y, x, li + 2)" :key="'p' + i" :x="x + .6" :y="y + .2" width=".2" height=".2" :fill="l.fleck"/>
    <!-- bevels on exposed edges only, so each letter reads as one solid block -->
    <rect v-for="([x, y], i) in l.topEdges" :key="'t' + i" :x="x" :y="y" width="1.01" height=".18" :fill="l.top"/>
    <rect v-for="([x, y], i) in l.leftEdges" :key="'l' + i" :x="x" :y="y" width=".14" height="1.01" :fill="l.top" opacity=".75"/>
    <rect v-for="([x, y], i) in l.bottomEdges" :key="'b' + i" :x="x" :y="y + .84" width="1.01" height=".16" :fill="l.edge"/>
    <rect v-for="([x, y], i) in l.rightEdges" :key="'r' + i" :x="x + .87" :y="y" width=".13" height="1.01" :fill="l.edge"/>
  </g>
  <g v-for="([x, y, c], i) in leaves" :key="'leaf' + i">
    <rect :x="x - .12" :y="y - .12" width=".84" height=".84" fill="#1b3013"/>
    <rect :x="x" :y="y" width=".6" height=".6" :fill="c"/>
    <rect :x="x" :y="y" width=".6" height=".15" fill="#c9f088" opacity=".6"/>
    <rect :x="x + .45" :y="y + .15" width=".15" height=".45" fill="#2f5d1c" opacity=".5"/>
  </g>
</svg>
</template>
