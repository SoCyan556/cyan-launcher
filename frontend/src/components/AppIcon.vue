<script setup>
import { House, Layers, Newspaper, Server, Settings, Play, ChevronDown, ArrowRight, Leaf, Minus, Square, Copy, X, Wrench, FolderOpen, LogIn, LogOut, RefreshCw, Download, Check, Package, MemoryStick, Monitor, Gamepad2, Users, Wifi, WifiOff, Clock, ExternalLink, CircleAlert, Sparkles, Blocks, Box, Folder, Image, Palette, Sun, Terminal, Info, CircleCheck, LoaderCircle, Search, RotateCcw } from '@lucide/vue'
defineProps({ name: { type: String, required: true }, size: { type: Number, default: 18 }, stroke: { type: Number, default: 1.8 } })
const icons = { House, Layers, Newspaper, Server, Settings, Play, ChevronDown, ArrowRight, Leaf, Minus, Square, Copy, X, Wrench, FolderOpen, LogIn, LogOut, RefreshCw, Download, Check, Package, MemoryStick, Monitor, Gamepad2, Users, Wifi, WifiOff, Clock, ExternalLink, CircleAlert, Sparkles, Blocks, Box, Folder, Image, Palette, Sun, Terminal, Info, CircleCheck, LoaderCircle, Search, RotateCcw }
</script>
<template><component :is="icons[name] || Box" :size="size" :stroke-width="stroke" aria-hidden="true"/></template>
