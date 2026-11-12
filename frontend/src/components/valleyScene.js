// Cached scenery, animated water and atmosphere. No video downloads or WebGL context.
export function createValley(canvas, seed = 7) {
  const W = 640, H = 400, water = 263
  canvas.width = W; canvas.height = H
  const g = canvas.getContext('2d', { alpha: false })
  const make = () => { const c = document.createElement('canvas'); c.width = W; c.height = H; return c }
  const sky = make(), terrain = make(), foreground = make()
  let value = seed
  const random = () => { value = (Math.imul(value, 1664525) + 1013904223) | 0; return (value >>> 0) / 4294967296 }
  const noise = (x, y) => { let n = Math.imul(x + 17, 374761393) ^ Math.imul(y + seed, 668265263); n = Math.imul(n ^ n >>> 13, 1274126177); return (n >>> 0) / 4294967296 }
  const rect = (c, x, y, w, h, color) => { c.fillStyle = color; c.fillRect(Math.round(x), Math.round(y), w, h) }
  const path = (c, points, color) => { c.fillStyle = color; c.beginPath(); points.forEach(([x, y], i) => i ? c.lineTo(x, y) : c.moveTo(x, y)); c.closePath(); c.fill() }
  const s = sky.getContext('2d'), land = terrain.getContext('2d'), front = foreground.getContext('2d')
  const gradient = s.createLinearGradient(0, 0, 0, water)
  ;[[0, '#101d36'], [.35, '#34436a'], [.68, '#997d98'], [.9, '#e4a895'], [1, '#f9c69c']].forEach(([p, c]) => gradient.addColorStop(p, c))
  s.fillStyle = gradient; s.fillRect(0, 0, W, H)
  const glow = s.createRadialGradient(300, 210, 5, 300, 210, 230)
  glow.addColorStop(0, '#ffd4a455'); glow.addColorStop(1, '#ffc69500'); s.fillStyle = glow; s.fillRect(0, 0, W, water)
  // A quiet crescent, with the warm horizon left open behind the launcher controls.
  s.fillStyle = '#f7e9ce'; s.beginPath(); s.arc(506, 58, 14, 0, Math.PI * 2); s.fill()
  s.fillStyle = '#263654'; s.beginPath(); s.arc(512, 53, 13, 0, Math.PI * 2); s.fill()
  const stars = Array.from({ length: 70 }, () => ({ x: random() * W, y: random() * 115, phase: random() * 6.28 }))
  function mountain(points, color, shade, snow = false) {
    path(land, [...points, [W, water + 8], [0, water + 8]], color)
    for (let i = 1; i < points.length - 1; i++) {
      const [x, y] = points[i], [nx, ny] = points[i + 1]
      if (y >= points[i - 1][1] || y >= ny) continue
      path(land, [[x, y], [nx, ny], [x + (nx - x) * .33, water], [x - 12, water]], shade)
      if (snow) {
        const left = points[i - 1]
        path(land, [[x, y], [x + (nx - x) * .29, y + (ny - y) * .29], [x + 11, y + 33], [x + 4, y + 24], [x - 2, y + 34], [x - 9, y + 22], [x + (left[0] - x) * .30, y + (left[1] - y) * .30]], '#d7dce4')
        path(land, [[x, y], [x + 4, y + 24], [x + 11, y + 33], [x + (nx - x) * .29, y + (ny - y) * .29]], '#9cacc7')
      }
    }
  }
  mountain([[0, 168], [53, 118], [88, 153], [143, 87], [188, 153], [237, 122], [288, 182], [350, 148], [401, 178], [472, 100], [525, 149], [575, 86], [640, 161]], '#7b87aa', '#67789a')
  mountain([[0, 209], [45, 168], [95, 201], [166, 132], [228, 211], [290, 226], [370, 183], [425, 216], [498, 107], [539, 165], [570, 139], [640, 201]], '#526481', '#3b526d', true)
  mountain([[0, 243], [55, 214], [104, 235], [160, 205], [220, 247], [310, 252], [381, 231], [440, 205], [496, 223], [554, 192], [640, 227]], '#304d5b', '#263f4c')
  const pine = (c, x, base, h, dark, light) => {
    rect(c, x, base - h * .4, 2, h * .4, '#26392e')
    for (let y = 0; y < h; y++) {
      const half = Math.round((h - y) * .25 * (1 - (y % 7) * .04))
      rect(c, x - half, base - h * .16 - y, half * 2 + 1, 1, dark)
      if (y % 3) rect(c, x - half, base - h * .16 - y, Math.max(1, half * .6), 1, light)
    }
  }
  for (let x = 0; x < W; x += 4 + random() * 5) {
    const valley = Math.exp(-(((x - 330) / 100) ** 2))
    const base = 250 + valley * 12 + Math.sin(x * .025) * 7
    pine(land, x, base, 10 + random() * 19, '#223e46', '#3b595b')
  }
  // Mossy, terraced cliff and a narrow waterfall on the right.
  path(land, [[505, 218], [519, 206], [559, 204], [571, 224], [594, 230], [612, 267], [491, 267]], '#354c4c')
  for (let y = 211; y < 265; y += 3) for (let x = 501; x < 583; x += 3) {
    if (noise(x, y) < .3) rect(land, x, y, 2 + Math.floor(noise(y, x) * 5), 2, y < 220 ? '#5a7563' : '#4b6060')
  }
  rect(land, 512, 206, 45, 3, '#749075')
  // Castle silhouette: small enough to feel distant, with warm inhabited windows.
  path(land, [[41, 257], [48, 230], [69, 219], [92, 225], [117, 254]], '#354b48')
  for (const [x, y, w, h] of [[52, 204, 11, 41], [85, 212, 10, 35], [64, 220, 22, 25]]) {
    rect(land, x, y, w, h, '#596267'); rect(land, x + w - 3, y, 3, h, '#384952')
    for (let bx = x; bx < x + w; bx += 4) rect(land, bx, y - 3, 2, 4, '#747878')
    for (let wy = y + 9; wy < y + h - 5; wy += 11) rect(land, x + 3, wy, 2, 3, '#edc58b')
  }
  rect(land, 74, 234, 5, 11, '#263d43')
  rect(land, 58, 191, 1, 13, '#646970')
  // Near banks frame the lake rather than covering the center of the composition.
  path(front, [[0, 285], [78, 299], [134, 335], [241, 400], [0, 400]], '#142f33')
  path(front, [[640, 273], [586, 286], [530, 320], [486, 400], [640, 400]], '#142d31')
  for (let x = -10; x < 185; x += 12 + random() * 12) pine(front, x, 323 + x * .31, 42 + random() * 52, '#10292d', '#24433c')
  for (let x = 525; x < 660; x += 14 + random() * 15) pine(front, x, 346 - (x - 525) * .3, 46 + random() * 61, '#10282c', '#29483f')
  // A cherry branch in the upper corner; shaded clusters, rather than a solid pink block.
  path(front, [[0, 19], [64, 8], [128, 0], [73, 18], [25, 38], [0, 51]], '#30333d')
  for (let i = 0; i < 145; i++) {
    const x = random() * 129, y = random() * 63 - 13
    if (y > 41 - x * .24 + random() * 15) continue
    rect(front, x, y, 3 + random() * 7, 2 + random() * 5, ['#805b7b', '#a97997', '#c397ac', '#e4b3c1'][Math.floor(random() * 4)])
  }
  const clouds = Array.from({ length: 9 }, () => ({ x: random() * W, y: 70 + random() * 110, w: 35 + random() * 80, speed: 1 + random() * 2, phase: random() * 6 }))
  const motes = Array.from({ length: 32 }, () => ({ x: random() * W, y: 290 + random() * 105, phase: random() * 6.28 }))
  function frame(t) {
    g.imageSmoothingEnabled = false; g.drawImage(sky, 0, 0)
    for (const p of stars) rect(g, p.x, p.y, 1, 1, `rgba(235,233,233,${.2 + (Math.sin(t * .65 + p.phase) + 1) * .23})`)
    for (const p of clouds) {
      const x = ((p.x + t * p.speed) % (W + p.w + 60)) - p.w
      g.globalAlpha = .17
      rect(g, x + 9, p.y - 3, p.w * .62, 3, '#f8d8c9'); rect(g, x, p.y, p.w, 4, '#f8d8c9'); rect(g, x + 14, p.y + 4, p.w * .85, 2, '#c7bbce')
    }
    g.globalAlpha = 1; g.drawImage(terrain, 0, 0)
    // Mirror the mountains in gently displaced horizontal strips.
    rect(g, 0, water, W, H - water, '#263f50')
    for (let y = water; y < H; y += 2) {
      const source = Math.max(0, water - (y - water) * .72)
      const shift = Math.round(Math.sin(y * .22 + t * .8) * (1 + (y - water) * .025))
      g.globalAlpha = .44 - (y - water) / 700
      g.drawImage(terrain, 0, source, W, 2, shift, y, W, 2)
    }
    g.globalAlpha = 1
    for (let i = 0; i < 50; i++) {
      const y = water + 4 + i * 2.3, x = (i * 83 + t * (i % 2 ? 1.2 : -.8) + W) % W
      rect(g, x, y, 3 + i % 9, 1, `rgba(197,193,191,${.06 + (i % 4) * .025})`)
    }
    // Falling water uses coherent ribbons, with a spreading mist at its foot.
    rect(g, 538, 209, 6, water - 208, '#84b7c9')
    for (let i = 0; i < 16; i++) rect(g, 539 + i % 3, 210 + (i * 5 + t * 29) % 51, i % 2 + 1, 3, '#c1e2e5')
    for (let i = 0; i < 7; i++) rect(g, 535 + Math.sin(t + i) * 6, water - 2 + i % 3, 5 + i % 4, 1, '#b2cfd6aa')
    // Mist drifts slowly across the valley and shoreline.
    g.globalAlpha = .1
    for (let i = 0; i < 5; i++) rect(g, 100 + Math.sin(t * .12 + i) * 32 + i * 73, 246 + i * 4, 125, 2, '#d5d9dc')
    g.globalAlpha = 1
    for (let i = 0; i < 5; i++) {
      const x = ((t * 5 + i * 19) % 850) - 80, y = 120 + Math.sin(t * .17 + i) * 7 + i * 2
      const wing = Math.sin(t * 4 + i) > 0 ? -1 : 1
      rect(g, x, y, 1, 1, '#38445b'); rect(g, x - 2, y + wing, 2, 1, '#38445b'); rect(g, x + 1, y + wing, 2, 1, '#38445b')
    }
    g.drawImage(foreground, 0, 0)
    // One tiny flag and a few drifting petals/fireflies finish the living scenery.
    rect(g, 59, 192, 6 + Math.round(Math.sin(t * 2)), 3, '#bb877d')
    for (const p of motes) {
      const alpha = Math.max(0, Math.sin(t * .9 + p.phase)) * .65
      rect(g, p.x + Math.sin(t * .25 + p.phase) * 9, p.y + Math.cos(t * .4 + p.phase) * 3, 1, 1, `rgba(221,221,152,${alpha})`)
    }
    for (let i = 0; i < 10; i++) rect(g, (i * 51 + t * 8) % W, (i * 35 + t * 11) % H, 2, 1, '#dfb2c177')
  }
  return frame
}
