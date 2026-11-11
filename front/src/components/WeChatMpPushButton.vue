<template>
  <!-- 同步到微信公众号：只在推送通道已配置时显示；默认只创建草稿，不自动发表 -->
  <q-btn v-if="configured" dense flat size="md" color="teal" icon="send" :label="showLabel ? '同步到公众号' : ''" :loading="pushing" :disable="pushing">
    <q-tooltip class="bg-teal">同步到微信公众号（创建草稿）</q-tooltip>
    <q-popup-proxy>
      <q-banner style="width: 340px;">
        <p>把「{{ postTitle }}」同步到微信公众号草稿箱？</p>
        <p class="text-caption text-grey-7">默认只创建草稿，不自动发表；发表/群发请在公众号后台或「公众号助手」App 手动完成（订阅号每天群发 1 次、一次最多 8 篇，文章进粉丝的订阅号消息列表）。</p>
        <q-checkbox v-model="publishTogether" label="同时提交发表" :disable="!autoPublish" dense />
        <p v-if="!autoPublish" class="text-caption text-orange">自动发表未开启（AutoPublish=false）；个人订阅号即使开启也可能报 48001，草稿不受影响。</p>
        <div class="row justify-end q-gutter-sm q-mt-sm">
          <q-btn flat size="sm" label="取消" color="grey" v-close-popup />
          <q-btn color="teal" size="sm" label="确认同步" @click="doPush" v-close-popup />
        </div>
      </q-banner>
    </q-popup-proxy>
  </q-btn>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { toast } from 'vue3-toastify'
import { WeChatMpApi } from '@/api/WeChatMpApi'

const props = defineProps<{
  /** 博客文章 Id */
  postId: number
  /** 文章标题（确认框里显示用） */
  postTitle: string
  /** 是否显示文字标签（写文章页用）；列表页默认只显示图标 */
  showLabel?: boolean
}>()

const configured = ref(false)
const autoPublish = ref(false)
const publishTogether = ref(false)
const pushing = ref(false)

onMounted(async () => {
  try {
    const res = await WeChatMpApi.getStatusCached()
    configured.value = !!res?.Data?.configured
    autoPublish.value = !!res?.Data?.autoPublish
  } catch {
    configured.value = false
  }
})

const doPush = async () => {
  pushing.value = true
  try {
    const res = await WeChatMpApi.push(props.postId, publishTogether.value)
    toast.success(res?.Message || '已同步到公众号草稿箱', { autoClose: 5000, position: 'top-center' })
    const warnings: string[] = res?.Data?.Warnings || []
    warnings.forEach((w) => toast.warning(w, { autoClose: 8000, position: 'top-center' }))
  } catch (e: any) {
    toast.error('同步失败：' + (e?.response?.data?.Message || e?.message || '未知错误'), { autoClose: 5000, position: 'top-center' })
  } finally {
    pushing.value = false
  }
}
</script>
