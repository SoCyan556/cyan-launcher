// Browser-only preview data. Never used inside the desktop app (the native host supplies real state).
const mods = [
  ['sodium', 'Sodium', 'The fastest rendering optimization mod for Minecraft.'],
  ['lithium', 'Lithium', 'Optimizes game physics, mob AI and block ticking without changing gameplay.'],
  ['ferrite-core', 'FerriteCore', 'Memory usage optimizations.'],
  ['entityculling', 'Entity Culling', 'Skips rendering entities you cannot see.'],
  ['immediatelyfast', 'ImmediatelyFast', 'Speeds up immediate-mode rendering such as HUDs and text.'],
  ['modmenu', 'Mod Menu', 'Adds a mod menu to view the list of mods you have installed.'],
  ['appleskin', 'AppleSkin', 'Food and saturation info on your HUD and in tooltips.'],
  ['mouse-tweaks', 'Mouse Tweaks', 'Enhances inventory management with mouse gestures.', true],
  ['jade', 'Jade', 'Shows information about what you are looking at.'],
  ['3dskinlayers', '3D Skin Layers', 'Renders the outer skin layer in 3D.', true],
  ['continuity', 'Continuity', 'Connected textures for glass, bookshelves and more.'],
  ['ambientsounds', 'AmbientSounds', 'Adds ambient sounds for biomes, caves and weather.'],
  ['fabric-api', 'Fabric API', 'Core library for Fabric mods.'],
  ['creativecore', 'CreativeCore', 'Library used by AmbientSounds.'],
].map(([slug, title, description, clientSide]) => ({ path: 'mods/' + slug + '.jar', title, description, iconUrl: null, version: '1.21.1', projectUrl: 'https://modrinth.com/mod/' + slug, clientSide: !!clientSide, enabled: true, installed: true }))

export function startPreview(state) {
  state.preview = true
  Object.assign(state.pack, {
    name: 'Comp Craft', packName: 'Cyan Civilization', tagline: 'Cyan Civilization, one click away.', modpackVersion: '1.0.0', minecraftVersion: '1.21.1', loader: 'Fabric 0.19.5',
    description: 'The Cyan Civilization modpack for Minecraft 1.21.1. The launcher installs everything and keeps it up to date.',
  })
  state.launcher = { version: '1.1.0', updating: false }
  state.modpack = { status: 'ready', mods, installed: false, installedVersion: null, updateAvailable: false }
  state.servers = [
    { id: 'survival', name: 'Comp Craft Survival', address: 'play.compcraft.example', port: 25565, description: 'The main whitelisted survival world. Runs the full modpack.', status: { online: true, players: 12, maxPlayers: 40, latency: 38, version: '1.21.1', motd: 'Cozy survival · whitelist open' } },
    { id: 'creative', name: 'Comp Craft Creative', address: 'creative.compcraft.example', port: 25565, description: 'Plots and build contests. Bring your best builds.', status: { online: false } },
  ]

  let timer = null
  return (type, payload) => {
    if (type === 'auth.signIn') {
      state.account.busy = true
      setTimeout(() => Object.assign(state.account, { busy: false, signedIn: true, username: 'Steve', uuid: '00000000-0000-0000-0000-000000000000' }), 900)
    } else if (type === 'auth.signOut') Object.assign(state.account, { signedIn: false, username: null })
    else if (type === 'modpack.toggle') { const m = state.modpack.mods.find(x => x.path === payload.path); if (m) m.enabled = payload.enabled }
    else if (type === 'game.play' || type === 'game.repair' || type === 'game.install') {
      const steps = [['preparing', 'Preparing Fabric'], ['installing', 'Installing Minecraft 1.21.1'], ['mods', 'Syncing mods'], ['launching', 'Starting Minecraft']]
      let p = 0
      clearInterval(timer)
      timer = setInterval(() => {
        p = Math.min(1, p + .018)
        const step = steps[Math.min(3, Math.floor(p * 4))]
        Object.assign(state.game, { phase: step[0], message: step[1], detail: `${Math.round(p * 2400)} / 2,400 files`, progress: p, canCancel: p < .99 })
        if (p >= 1) { clearInterval(timer); if (type === 'game.play') Object.assign(state.game, { phase: 'running', message: payload.id ? 'Joining server' : 'Minecraft is running', detail: '', canCancel: false }); else { state.modpack.installed = true; state.modpack.installedVersion = '1.0.0'; Object.assign(state.game, { phase: 'idle', message: '', detail: '', canCancel: false, notice: 'Everything is installed. Press Play!' }) } }
      }, 60)
    } else if (type === 'game.cancel' || type === 'game.stop' || type === 'game.dismiss') { clearInterval(timer); Object.assign(state.game, { phase: 'idle', message: '', progress: 0, canCancel: false, notice: null }) }
    else if (type === 'servers.refresh') { state.serversBusy = true; setTimeout(() => state.serversBusy = false, 900) }
    else if (type === 'modpack.refresh') { state.modpack.status = 'resolving'; setTimeout(() => state.modpack.status = 'ready', 900) }
  }
}
