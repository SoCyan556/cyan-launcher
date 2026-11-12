import { ref, computed } from 'vue'
import { state } from './state'

// Same physical springs as ELIXR so the two apps feel related.
const preference = window.matchMedia('(prefers-reduced-motion: reduce)')
const systemReduced = ref(preference.matches)
preference.addEventListener('change', e => systemReduced.value = e.matches)
export const reducedMotion = computed(() => systemReduced.value || !!state.settings.reduceMotion)

export const spring = { type: 'spring', stiffness: 380, damping: 29, mass: .85 }
export const softSpring = { type: 'spring', stiffness: 290, damping: 25, mass: .8 }
export const popSpring = { type: 'spring', stiffness: 520, damping: 30, mass: .7 }
export const press = computed(() => reducedMotion.value ? {} : { scale: .96 })
export function enterRow(index) { return reducedMotion.value ? { duration: 0 } : { ...softSpring, delay: Math.min(index, 9) * .035 } }
