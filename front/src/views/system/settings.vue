<template>
<div class="system-settings-page">
  <!-- 顶部操作栏 -->
  <div class="row">
    <div class="col text-right">
      <q-btn id="save" color="primary" size="lg" icon="save" label="保存配置" @click="saveSettings" :loading="saving" />
    </div>
  </div>
  <!-- 基础配置 -->
  <q-card flat bordered>
    <q-card-section>
      <div class="text-h6 q-mb-md">基础配置</div>
      <div class="row q-gutter-md q-mb-md">
        <div class="col">
          <q-input v-model="settings.Domain" label="网站主域名" outlined dense />
        </div>
        <div class="col">
          <q-input v-model="settings.Title" label="网站标语" outlined dense />
        </div>
      </div>
      <div class="row q-gutter-md">
        <div class="col">
          <q-input v-model="settings.Slogan" label="网站口号" outlined dense />
        </div>
        <div class="col">
          <q-input v-model="settings.ReservedName" label="保留关键字" outlined dense hint="用逗号分隔多个关键字" />
        </div>
      </div>
      <div class="row q-gutter-md">
        <div class="col">
          <q-input v-model="settings.PathRoot" label="资源管理器根目录" outlined dense>
            <template v-slot:append>
              <q-btn flat round dense icon="folder" @click="testPath" />
            </template>
          </q-input>
        </div>
        <div class="col">
          <q-input v-model="settings.UploadPath" label="文件上传目录" outlined dense />
        </div>
        <div class="col">
          <div class="row q-mb-md">
            <div class="col">
              <q-checkbox v-model="enableRss" label="启用RSS" />
            </div>
            <div class="col">
              <q-checkbox v-model="enableDonate" label="启用网站打赏功能" />
            </div>
          </div>
        </div>
        <div class="col">
          <div class="row q-mb-md">
            <div class="col">
              <q-checkbox v-model="closeSite" label="闭站保护" />
            </div>
            <div class="col">
              <q-checkbox v-model="dataReadonly" label="数据写保护" />
            </div>
            <div class="col">
              <q-checkbox v-model="defaultSafeMode" label="默认安全模式" />
            </div>
          </div>
        </div>
      </div>
      <div class="row q-gutter-md q-mb-md">
        <div class="col">
          <div class="row q-gutter-md">
            <div class="col">
              <q-select v-model="settings.WatermarkType" :options="watermarkOptions" label="水印类型" outlined dense emit-value map-options />
            </div>
            <div class="col">
              <div v-if="settings.WatermarkType == 'Text'">
                <q-input autogrow dense outlined v-model="settings.Watermark" placeholder="留空则上传图片时不添加水印" />
              </div>
              <div v-if="settings.WatermarkType == 'Image'">
                <q-img :src="settings.WatermarkImage?.startsWith('http') ? settings.WatermarkImage : globalConfig.baseURL + settings.WatermarkImage" spinner-color="white" @click="showImageUpload('WatermarkImage')" bordered class="logo-preview cursor-pointer" style="max-height: 200px; border: 1px dashed #ccc;" />
                <q-btn color="primary" @click="showImageUpload('WatermarkImage')" label="上传"></q-btn>
              </div>
            </div>
            <div class="col" v-if="settings.WatermarkType != 'None'">
              <q-select v-model="settings.WatermarkPosition" :options="[{ label: '左上角', value: '0' }, { label: '右上角', value: '1' }, { label: '左下角', value: '2' }, { label: '右下角', value: '3' }, { label: '中间', value: '4' }]" outlined dense emit-value map-options label="水印位置："> </q-select>
            </div>
          </div>
        </div>
        <div class="col">
          <div class="row" v-if="enableRss">
            <q-select class="col-auto" v-model="settings.RssStart" :options="[{ label: '1天前', value: '-1' }, { label: '2天前', value: '-2' }, { label: '3天前', value: '-3' }, { label: '4天前', value: '-4' }, { label: '5天前', value: '-5' }]" dense emit-value map-options borderless>
              <template v-slot:prepend>
                <span style="font-size: 14px;"> RSS订阅从 </span>
              </template>
            </q-select>
            <q-select class="col-auto" v-model="settings.RssEnd" :options="[{ label: '现在', value: '0' }, { label: '2小时前', value: '-2' }, { label: '5小时前', value: '-5' }, { label: '12小时前', value: '-12' }, { label: '1天前', value: '-24' }, { label: '2天前', value: '-48' }]" dense emit-value map-options borderless>
              <template v-slot:prepend>
                <span style="font-size: 14px;"> 到 </span>
              </template>
            </q-select>
          </div>
        </div>
      </div>
    </q-card-section>
  </q-card>
  <!-- 自定义脚本和样式 -->
  <q-card flat bordered>
    <q-card-section>
      <div class="text-h6">自定义脚本和样式</div>
      <div>
        <div class="text-subtitle1">CSS样式代码：</div>
        <div ref="stylesEditor" class="editor-container" style="height: 200px;"></div>
      </div>
      <div>
        <div class="text-subtitle1 q-mb-sm">JS脚本代码：</div>
        <div ref="scriptsEditor" class="editor-container" style="height: 400px;"></div>
      </div>
    </q-card-section>
  </q-card>
  <!-- SEO 配置 -->
  <q-card flat bordered>
    <q-card-section>
      <div class="text-h6">网站SEO相关</div>
      <div class="row q-gutter-md q-mb-md">
        <div class="col">
          <q-input v-model="settings.Title" label="SEO标题" outlined dense />
        </div>
        <div class="col">
          <q-input v-model="settings.Keyword" label="SEO关键词" outlined dense />
        </div>
      </div>
      <div class="row q-gutter-md">
        <div class="col">
          <q-input v-model="settings.Description" label="SEO描述" outlined dense autogrow="" />
        </div>
      </div>
    </q-card-section>
  </q-card>
  <!-- 邮箱配置 -->
  <q-card flat bordered>
    <q-card-section>
      <div class="text-h6">网站邮箱配置</div>
      <div class="row q-gutter-md q-mb-md">
        <div class="col">
          <q-input v-model="settings.EmailFrom" label="发件邮箱" outlined dense type="email" />
        </div>
        <div class="col">
          <q-input v-model="settings.EmailPwd" label="发件邮箱密码" outlined dense type="password" />
        </div>
        <div class="col">
          <q-input v-model="settings.SMTP" label="SMTP服务器" outlined dense />
        </div>
        <div class="col">
          <q-input v-model="settings.SmtpPort" label="SMTP端口号" outlined dense type="number" />
        </div>
      </div>
      <div class="row q-gutter-md">
        <div class="col">
          <q-checkbox v-model="enableSsl" label="启用SSL加密" />
        </div>
        <div class="col">
          <q-input v-model="settings.ReceiveEmail" label="接收网站通知邮箱" outlined dense type="email">
            <template v-slot:append>
              <q-btn dense color="info" label="测试发送" @click="testEmail" :loading="testing" />
            </template>
          </q-input>
        </div>
      </div>
    </q-card-section>
  </q-card>
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
  <!-- 媒体设置 -->
  <q-card flat bordered>
    <q-card-section>
      <div class="text-h6">网站媒体设置</div>
      <div class="row q-gutter-md">
        <div class="col-2">
          <div class="text-subtitle1">网站Logo：</div>
          <q-img :src="settings.logo?.startsWith('http') ? settings.logo : globalConfig.baseURL + settings.logo" class="logo-preview cursor-pointer" @click="showImageUpload('logo')" style="max-height: 200px; border: 1px dashed #ccc;" />
          <q-btn flat size="sm" label="选择图片" @click="showImageUpload('logo')" class="full-width q-mt-sm" />
        </div>
        <div class="col-12">
          <div class="text-subtitle1">文章版权声明：</div>
          <vue-ueditor-wrap v-model="settings.Disclaimer" :config="{ initialFrameHeight: 200, initialFrameWidth: '100%' }" />
        </div>
        <div class="col-12">
          <div class="text-subtitle1">网站页脚版权：</div>
          <vue-ueditor-wrap v-model="settings.Copyright" :config="{ initialFrameHeight: 120, initialFrameWidth: '100%' }" />
        </div>
      </div>
    </q-card-section>
  </q-card>
  <!-- 图片上传对话框 -->
  <q-dialog v-model="showUploadDialog" persistent>
    <q-card style="min-width: 400px">
      <q-card-section>
        <div class="text-h6">上传图片</div>
      </q-card-section>
      <q-card-section>
        <q-file v-model="uploadFile" label="选择图片文件" outlined accept="image/*" />
      </q-card-section>
      <q-card-actions align="right">
        <q-btn flat label="取消" color="grey" @click="cancelUpload" />
        <q-btn flat label="上传" color="primary" @click="uploadImage" :loading="uploading" />
      </q-card-actions>
    </q-card>
  </q-dialog>
</div>
</template>
<script setup lang="ts">
import { ref, onMounted, onUnmounted, computed, nextTick } from 'vue'
import { toast } from 'vue3-toastify'
import api from '@/axios/AxiosConfig'
import globalConfig from '@/config'
import loadCodeMirror from '../../utils/codeMirrorLoader'

// 定义接口类型
interface Settings {
  Domain?: string
  Title?: string
  SubTitle?: string
  Keywords?: string
  WatermarkType?: string
  WatermarkOpacity?: string
  EnableRss?: string
  EnableDonate?: string
  CloseSite?: string
  DataReadonly?: string
  DefaultSafeMode?: string
  EnableSsl?: string
  RssTitle?: string
  RssDescription?: string
  PathRoot?: string
  UploadPath?: string
  Styles?: string
  Scripts?: string
  SiteTitle?: string
  SiteKeywords?: string
  SiteDescription?: string
  EmailFrom?: string
  EmailPwd?: string
  SMTP?: string
  SmtpPort?: string
  ReceiveEmail?: string
  McpPublishToken?: string
  McpPublishEnabled?: string
  logo?: string
  Disclaimer?: string
  Copyright?: string
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
const testing = ref(false)
const uploading = ref(false)
const showUploadDialog = ref(false)
const uploadFile = ref(null)
const currentImageProperty = ref('')

// 编辑器引用
const stylesEditor = ref()
const scriptsEditor = ref()
let styleCodeMirrorEditor: any = null
let scriptCodeMirrorEditor: any = null

const destroyEditors = () => {
  const disposeEditor = (editorRef: any, containerRef: any) => {
    if (editorRef) {
      const wrapper = editorRef.getWrapperElement?.()
      if (wrapper && wrapper.parentNode) {
        wrapper.parentNode.removeChild(wrapper)
      }
    }

    if (containerRef?.value) {
      containerRef.value.innerHTML = ''
    }
  }

  disposeEditor(styleCodeMirrorEditor, stylesEditor)
  disposeEditor(scriptCodeMirrorEditor, scriptsEditor)

  styleCodeMirrorEditor = null
  scriptCodeMirrorEditor = null
}

// 水印选项
const watermarkOptions = [
  { label: '无水印', value: 'None' },
  { label: '文字水印', value: 'Text' },
  { label: '图片水印', value: 'Image' }
]

// 计算属性
const enableRss = computed({
  get: () => settings.value.EnableRss === 'true',
  set: (val: boolean) => { settings.value.EnableRss = val ? 'true' : 'false' }
})

const enableDonate = computed({
  get: () => settings.value.EnableDonate === 'true',
  set: (val: boolean) => { settings.value.EnableDonate = val ? 'true' : 'false' }
})

const closeSite = computed({
  get: () => settings.value.CloseSite === 'true',
  set: (val: boolean) => { settings.value.CloseSite = val ? 'true' : 'false' }
})

const dataReadonly = computed({
  get: () => settings.value.DataReadonly === 'true',
  set: (val: boolean) => { settings.value.DataReadonly = val ? 'true' : 'false' }
})

const defaultSafeMode = computed({
  get: () => settings.value.DefaultSafeMode !== 'false',
  set: (val: boolean) => { settings.value.DefaultSafeMode = val ? 'true' : 'false' }
})

const enableSsl = computed({
  get: () => settings.value.EnableSsl === 'true',
  set: (val: boolean) => { settings.value.EnableSsl = val ? 'true' : 'false' }
})

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
      headers: { 'Content-Type': 'application/json', 'Authorization': 'Bearer ' + token },
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

// 加载系统设置
const loadSettings = async () => {
  const response = await api.get('/system/getsettings') as ApiResponse
  if (response?.Success && response.Data) {
    const settingsObj: Settings = {}
    response.Data.forEach((item: any) => {
      settingsObj[item.Name] = item.Value
    })
    settings.value = settingsObj

    // 初始化编辑器内容
    if (styleCodeMirrorEditor) {
      styleCodeMirrorEditor.setValue(settings.value.Styles || '')
    }
    if (scriptCodeMirrorEditor) {
      scriptCodeMirrorEditor.setValue(settings.value.Scripts || '')
    }
  } else {
    toast.error('获取系统设置失败', { autoClose: 2000, position: 'top-center' })
  }
}

// 保存系统设置
const saveSettings = async () => {
  saving.value = true
  // 更新编辑器内容到设置
  if (styleCodeMirrorEditor) {
    settings.value.Styles = styleCodeMirrorEditor.getValue()
  }
  if (scriptCodeMirrorEditor) {
    settings.value.Scripts = scriptCodeMirrorEditor.getValue()
  }

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

// 测试邮件发送
const testEmail = async () => {
  testing.value = true
  try {
    const response = await api.post('/system/mailtest', {
      smtp: settings.value.SMTP,
      user: settings.value.EmailFrom,
      pwd: settings.value.EmailPwd,
      port: settings.value.SmtpPort,
      to: settings.value.ReceiveEmail,
      ssl: enableSsl.value
    }) as ApiResponse

    if (response?.Success) {
      toast.success('邮件发送成功', { autoClose: 2000, position: 'top-center' })
    } else {
      toast.error(response?.Message || '邮件发送失败', { autoClose: 2000, position: 'top-center' })
    }
  } catch (error) {
    toast.error('邮件发送失败', { autoClose: 2000, position: 'top-center' })
    console.error('Error testing email:', error)
  } finally {
    testing.value = false
  }
}

// 测试路径
const testPath = async () => {
  try {
    const response = await api.post('/system/pathtest', {
      path: settings.value.PathRoot
    }) as ApiResponse

    if (response?.Success) {
      toast.success('路径测试成功', { autoClose: 2000, position: 'top-center' })
    } else {
      toast.error(response?.Message || '路径测试失败', { autoClose: 2000, position: 'top-center' })
    }
  } catch (error) {
    toast.error('路径测试失败', { autoClose: 2000, position: 'top-center' })
    console.error('Error testing path:', error)
  }
}

// 显示图片上传对话框
const showImageUpload = (property: string) => {
  currentImageProperty.value = property
  showUploadDialog.value = true
}

// 取消上传
const cancelUpload = () => {
  showUploadDialog.value = false
  uploadFile.value = null
  currentImageProperty.value = ''
}

// 上传图片
const uploadImage = async () => {
  if (!uploadFile.value) {
    toast.error('请选择图片文件', { autoClose: 2000, position: 'top-center' })
    return
  }

  uploading.value = true
  try {
    const formData = new FormData()
    formData.append('file', uploadFile.value)

    const response = await api.post('/Upload', formData, {
      headers: { 'Content-Type': 'multipart/form-data' }
    }) as ApiResponse

    if (response?.Success && response.Data) {
      settings.value[currentImageProperty.value] = response.Data
      toast.success('图片上传成功', { autoClose: 2000, position: 'top-center' })
      cancelUpload()
    } else {
      toast.error(response?.Message || '图片上传失败', { autoClose: 2000, position: 'top-center' })
    }
  } catch (error) {
    toast.error('图片上传失败', { autoClose: 2000, position: 'top-center' })
    console.error('Error uploading image:', error)
  } finally {
    uploading.value = false
  }
}

// 初始化CodeMirror编辑器
const initEditors = () => {
  const CodeMirror = (window as any).CodeMirror
  if (!CodeMirror) {
    return
  }

  destroyEditors()

  if (stylesEditor.value) {
    styleCodeMirrorEditor = CodeMirror(stylesEditor.value, {
      value: settings.value.Styles || '',
      lineNumbers: true,
      mode: 'css'
    })
  }

  if (scriptsEditor.value) {
    scriptCodeMirrorEditor = CodeMirror(scriptsEditor.value, {
      value: settings.value.Scripts || '',
      lineNumbers: true,
      mode: 'javascript'
    })
  }
}

// 生命周期钩子
onMounted(async () => {
  document.querySelector('#save').scrollIntoView({ behavior: 'smooth', block: 'center' })
  await loadSettings()
  try {
    await loadCodeMirror()
    await nextTick()
    // 延迟初始化编辑器，确保DOM已渲染
    setTimeout(initEditors, 100)
  } catch (error) {
    toast.error('编辑器初始化失败', { autoClose: 2000, position: 'top-center' })
    console.error('Error loading CodeMirror assets:', error)
  }
})

onUnmounted(() => {
  destroyEditors()
})
</script>
<style scoped lang="scss">
.system-settings-page {
  padding: 20px;
  margin: 0 auto;
}

.editor-container {
  border: 1px solid #ddd;
  border-radius: 4px;
}

:deep(.CodeMirror) {
  height: 100%;
}

:deep(.CodeMirror-scroll) {
  height: 100%;
}

.logo-preview {
  border-radius: 4px;
  transition: all 0.3s ease;

  &:hover {
    opacity: 0.8;
  }
}

.q-card {
  margin-bottom: 16px;
}

// 响应式设计
@media (max-width: 768px) {
  .system-settings-page {
    padding: 10px;
  }

  .row.q-gutter-md {

    .col-md-6,
    .col-md-4,
    .col-md-3 {
      margin-bottom: 16px;
    }
  }
}
</style>