<template><iframe ref="frame" :src="globalConfig.baseURL + '/License'" width="100%" frameborder="0" scrolling="yes" @load="handleLoad"></iframe></template>
<script setup>
import { ref } from 'vue'
import { globalConfig } from '@/config'

const frame = ref(null)
const handleLoad = () => {
  if (frame.value) {
    frame.value.style.height = 'calc(100vh - 95px)'
  }
}
</script>
