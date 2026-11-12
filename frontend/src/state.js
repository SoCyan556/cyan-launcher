import { reactive } from 'vue'

export const state = reactive({
  host: false,
  preview: false,
  pack: { name: 'Comp Craft', packName: null, tagline: '', description: '', modpackVersion: '', minecraftVersion: '1.21.1', loader: 'Fabric' },
  settings: { memoryMb: 6144, width: 1280, height: 720, fullscreen: false, afterLaunch: 'minimize', jvmArguments: '', reduceMotion: false },
  system: { memoryMb: 16384 },
  account: { signedIn: false, username: null, uuid: null, busy: false, error: null },
  game: { phase: 'idle', message: '', detail: '', progress: 0, canCancel: false, notice: null },
  modpack: { status: 'idle', mods: [], installed: false, installedVersion: null, updateAvailable: false },
  launcher: { version: '', updating: false },
  servers: [],
  serversBusy: false,
  window: { maximized: false },
  notice: null,
})

const bridge = window.chrome?.webview
let previewHandler = null

export function send(type, payload = {}) {
  if (bridge) bridge.postMessage({ type, ...payload })
  else previewHandler?.(type, payload)
}

export async function start() {
  if (bridge) {
    bridge.addEventListener('message', ({ data }) => {
      if (data.type === 'state') { Object.assign(state, data.payload); state.host = true }
      else if (data.type === 'notice') state.notice = data.message
    })
  } else {
    // Plain browser (npm run dev): fictional data so the UI can be designed without the desktop host.
    const { startPreview } = await import('./preview.js')
    previewHandler = startPreview(state)
  }
  send('ready')
}

export function updateSetting(key, value) {
  state.settings[key] = value
  send('settings.set', { key, value })
}

export const busyPhases = ['preparing', 'installing', 'mods', 'launching']
