<template>
<div class="system-mcp-page">
  <!-- 顶部操作栏 -->
  <div class="row">
    <div class="col text-right">
      <q-btn id="save" color="primary" size="lg" icon="save" label="保存配置" @click="saveSettings" :loading="saving" />
    </div>
  </div>
  <!-- MCP 智能体配置 -->
  <q-card flat bordered>
    <q-card-section>
      <div class="text-h6">MCP 智能体发布</div>
      <div class="row q-gutter-md items-center">
        <div class="col-auto">
          <q-checkbox v-model="mcpEnabled" label="启用 MCP 接口" />
        </div>
        <div class="col">
          <div class="text-caption text-grey">开启后，外部 AI 智能体可凭令牌通过 MCP 接口读取、发布和维护本站文章；关闭后接口一律拒绝访问（开关与令牌修改后需点右上角「保存配置」生效）。</div>
        </div>
      </div>
      <div class="row q-gutter-md">
        <div class="col">
          <q-input v-model="settings.McpPublishToken" label="MCP 发布令牌（Bearer）" outlined dense :type="showMcpToken ? 'text' : 'password'" hint="智能体调用接口时须在请求头携带 Authorization: Bearer <令牌>；请使用足够长的随机字符串，泄露后点「生成新令牌」并保存即可轮换">
            <template v-slot:append>
              <q-btn dense flat :label="showMcpToken ? '隐藏' : '显示'" @click="showMcpToken = !showMcpToken" />
              <q-btn dense flat label="生成新令牌" @click="generateMcpToken" />
              <q-btn dense flat label="复制" @click="copyText(settings.McpPublishToken || '')" />
            </template>
          </q-input>
        </div>
      </div>
      <div class="row q-gutter-md items-end">
        <div class="col">
          <q-input :model-value="mcpEndpoint" label="MCP 接口地址" outlined dense readonly hint="将此地址与令牌填入智能体客户端的 MCP 配置即可连接本站">
            <template v-slot:append>
              <q-btn dense flat label="复制" @click="copyText(mcpEndpoint)" />
            </template>
          </q-input>
        </div>
        <div class="col-auto">
          <q-btn color="primary" label="测试连接" @click="testMcpConnection" :loading="testingMcp" />
        </div>
      </div>
      <div class="row items-center q-mt-md">
        <div class="text-subtitle2">客户端配置示例（JSON）</div>
        <q-space />
        <q-btn dense flat size="sm" label="复制 JSON" @click="copyText(mcpClientConfig)" />
      </div>
      <pre style="background: #f5f5f5; border: 1px solid #ddd; border-radius: 4px; padding: 12px; overflow-x: auto; font-size: 12px; margin: 4px 0 0;">{{ mcpClientConfig }}</pre>
      <div class="text-subtitle2 q-mt-md">可用工具（共 7 个）</div>
      <div class="text-body2">
        list_categories 查看文章分类 · list_posts 分页查询文章 · get_post 读取文章全文 · list_tags 查看热门标签 · publish_post 发布新文章 · update_post 修改/上下架文章 · upload_image 上传图片到图床
      </div>
    </q-card-section>
  </q-card>
</div>
</template>

<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { toast } from 'vue3-toastify'
import api from '@/axios/AxiosConfig'

// 定义接口类型（与系统设置页共用同一套设置存取接口，这里只维护 MCP 相关键，其余键原样回存）
interface Settings {
  McpPublishToken?: string
  McpPublishEnabled?: string
  [key: string]: any
}

interface ApiResponse {
  Success: boolean
  Message: string
  Data?: any
}

// 响应式数据
const settings = ref<Settings>({})
const saving = ref(false)

// MCP 启用开关（设置键缺失时默认启用，与后端「配了令牌即启用」的旧行为一致）
const mcpEnabled = computed({
  get: () => settings.value.McpPublishEnabled !== 'false',
  set: (val: boolean) => { settings.value.McpPublishEnabled = val ? 'true' : 'false' }
})

const showMcpToken = ref(false)
const testingMcp = ref(false)

// MCP 接口地址（管理后台与站点同源）
const mcpEndpoint = computed(() => window.location.origin + '/api/mcp')

// 客户端配置示例（自动带入当前填写的令牌）
const mcpClientConfig = computed(() => JSON.stringify({
  mcpServers: {
    'masuit-blog': {
      url: mcpEndpoint.value,
      headers: { Authorization: 'Bearer ' + (settings.value.McpPublishToken || '<你的令牌>') }
    }
  }
}, null, 2))

// 生成随机令牌（24 字节随机数，48 位十六进制）
const generateMcpToken = () => {
  const bytes = new Uint8Array(24)
  window.crypto.getRandomValues(bytes)
  settings.value.McpPublishToken = Array.from(bytes, b => b.toString(16).padStart(2, '0')).join('')
  toast.success('已生成新令牌，点右上角「保存配置」后生效', { autoClose: 2500, position: 'top-center' })
}

// 复制文本到剪贴板
const copyText = async (text: string) => {
  try {
    await navigator.clipboard.writeText(text)
    toast.success('已复制', { autoClose: 1500, position: 'top-center' })
  } catch {
    toast.error('复制失败，请手动选中复制', { autoClose: 2000, position: 'top-center' })
  }
}

// 测试 MCP 连接：用当前表单中的令牌调用 tools/list（注意：服务端校验的是已保存的令牌）
const testMcpConnection = async () => {
  const token = (settings.value.McpPublishToken || '').trim()
  if (!token) {
    toast.error('请先填写 MCP 发布令牌', { autoClose: 2000, position: 'top-center' })
    return
  }
  testingMcp.value = true
  try {
    const resp = await fetch(mcpEndpoint.value, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Authorization: 'Bearer ' + token },
      body: JSON.stringify({ jsonrpc: '2.0', id: 1, method: 'tools/list' })
    })
    if (resp.status === 401) {
      toast.error('连接失败：令牌不正确（若刚修改过令牌，请先点右上角保存）', { autoClose: 3000, position: 'top-center' })
      return
    }
    if (resp.status === 503) {
      toast.error('连接失败：MCP 接口未启用（请开启开关并保存，且令牌不能为空）', { autoClose: 3000, position: 'top-center' })
      return
    }
    const data = await resp.json()
    const count = data?.result?.tools?.length
    if (resp.ok && typeof count === 'number') {
      toast.success(`连接成功，共 ${count} 个可用工具`, { autoClose: 2500, position: 'top-center' })
    } else {
      toast.error('连接失败：接口返回异常', { autoClose: 2500, position: 'top-center' })
    }
  } catch (error) {
    toast.error('连接失败：网络或接口异常', { autoClose: 2500, position: 'top-center' })
    console.error('Error testing MCP connection:', error)
  } finally {
    testingMcp.value = false
  }
}

// 加载系统设置（全量加载，保存时全量回存，避免覆盖其他设置项）
const loadSettings = async () => {
  const response = await api.get('/system/getsettings') as ApiResponse
  if (response?.Success && response.Data) {
    const settingsObj: Settings = {}
    response.Data.forEach((item: any) => {
      settingsObj[item.Name] = item.Value
    })
    settings.value = settingsObj
  } else {
    toast.error('获取系统设置失败', { autoClose: 2000, position: 'top-center' })
  }
}

// 保存系统设置
const saveSettings = async () => {
  saving.value = true
  const response = await api.post('/system/save', Object.keys(settings.value).map(key => {
    return { Name: key, Value: settings.value[key] }
  })) as ApiResponse
  if (response?.Success) {
    toast.success(response.Message || '保存成功', { autoClose: 2000, position: 'top-center' })
  } else {
    toast.error(response?.Message || '保存失败', { autoClose: 2000, position: 'top-center' })
  }
  saving.value = false
}

onMounted(() => {
  loadSettings()
})
</script>
