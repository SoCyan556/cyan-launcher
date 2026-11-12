<script setup>
import AppIcon from './AppIcon.vue'
import { motion } from 'motion-v'
import { spring, reducedMotion, press } from '../motion'
defineProps({ modelValue: Boolean, label: String, hint: String, icon: String, disabled: Boolean })
defineEmits(['update:modelValue'])
</script>
<template>
<motion.button type="button" class="toggle-control" role="switch" :aria-label="label" :aria-checked="modelValue" :disabled="disabled" :while-press="press" :transition="spring" @click="$emit('update:modelValue', !modelValue)">
  <span class="toggle-label"><AppIcon v-if="icon" :name="icon" :size="17"/><span>{{ label }}<small v-if="hint">{{ hint }}</small></span></span>
  <span class="switch-track" :class="{ on: modelValue }"><motion.span :animate="{ x: modelValue ? 16 : 0 }" :initial="false" :transition="reducedMotion ? { duration: 0 } : { type: 'spring', stiffness: 520, damping: 23 }"/></span>
</motion.button>
</template>
