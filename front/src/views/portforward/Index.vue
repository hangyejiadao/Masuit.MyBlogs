<template>
<div class="portforward-container">
  <div class="text-h4 q-mb-lg">端口转发</div>
  <div class="q-mb-lg">
    <div class="row items-center justify-between">
      <div class="col-auto">
        <div class="q-gutter-sm">
          <q-btn color="positive" icon="add" label="新增规则" @click="openCreate" />
          <q-btn color="primary" icon="refresh" label="刷新" @click="load" :loading="loading" />
        </div>
      </div>
      <div class="col-auto">
        <q-input v-model="keyword" outlined dense placeholder="搜索名称或目标" @keyup.enter="reload">
          <template v-slot:prepend>
            <q-icon name="search" />
          </template>
        </q-input>
      </div>
    </div>
  </div>

  <q-card>
    <q-markup-table flat separator="cell">
      <thead>
        <tr>
          <th class="text-left">名称</th>
          <th class="text-center">监听端口</th>
          <th class="text-left">目标</th>
          <th class="text-left">路径</th>
          <th class="text-center">启用</th>
          <th class="text-center">监听状态</th>
          <th class="text-center">活跃连接</th>
          <th class="text-right">累计流量</th>
          <th class="text-left">备注</th>
          <th class="text-center">操作</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="row in rows" :key="row.Id">
          <td class="text-left">{{ row.Name }}</td>
          <td class="text-center">{{ row.ListenPort }}</td>
          <td class="text-left">{{ row.TargetHost }}:{{ row.TargetPort }}</td>
          <td class="text-left">{{ row.Path || '-' }}</td>
          <td class="text-center">
            <q-toggle :model-value="row.Enabled" dense @update:model-value="toggleEnabled(row)" />
          </td>
          <td class="text-center">
            <q-badge v-if="!row.Enabled" color="grey" label="已停用" />
            <q-badge v-else-if="row.IsListening" color="positive" label="监听中" />
            <q-badge v-else color="negative" :label="row.StatusMessage || '未启动'">
              <q-tooltip v-if="row.StatusMessage">{{ row.StatusMessage }}</q-tooltip>
            </q-badge>
          </td>
          <td class="text-center">{{ row.ActiveConnections }}</td>
          <td class="text-right">{{ formatBytes(row.TotalBytes) }}</td>
          <td class="text-left">{{ row.Remark || '-' }}</td>
          <td class="text-center">
            <q-btn flat dense size="sm" color="primary" label="编辑" @click="openEdit(row)" />
            <q-btn flat dense size="sm" color="negative" label="删除" @click="askDelete(row)" />
          </td>
        </tr>
        <tr v-if="!loading && rows.length === 0">
          <td colspan="10" class="text-center text-grey">暂无数据</td>
        </tr>
      </tbody>
    </q-markup-table>
    <div class="row items-center justify-between q-pa-md">
      <div class="row items-center q-gutter-sm">
        <span class="text-grey">共 {{ total }} 条</span>
        <q-select v-model="size" :options="[10, 20, 50]" dense outlined label="每页" @update:model-value="reload" />
      </div>
      <q-pagination v-model="page" :max="maxPage" boundary-numbers direction-links @update:model-value="load" />
    </div>
  </q-card>

  <q-dialog v-model="showDialog" persistent>
    <q-card style="min-width: 460px">
      <q-card-section>
        <div class="text-h6">{{ form.Id > 0 ? '编辑规则' : '新增规则' }}</div>
      </q-card-section>
      <q-card-section class="q-gutter-md">
        <q-input v-model="form.Name" outlined dense label="规则名称 *" />
        <q-input v-model.number="form.ListenPort" outlined dense type="number" min="10000" max="10100" label="监听端口 *"
          hint="只能填 10000-10100：博客运行在 Docker 容器中，只有这个端口段会发布到宿主机，超出范围外部无法访问" />
        <q-input v-model="form.TargetHost" outlined dense label="目标地址 *（IP 或主机名）" />
        <q-input v-model.number="form.TargetPort" outlined dense type="number" min="1" max="65535" label="目标端口 *" />
        <q-input v-model="form.Path" outlined dense label="路径（可选，如 /mcp）"
          hint="留空不启用；填了如 /mcp 后，访问博客该路径的 HTTP 请求会被代理转发到目标并剥掉此前缀（例：访问 /mcp/mcp/foo 实际请求目标的 /mcp/foo），与监听端口相互独立、可同时启用" />
        <div class="row items-center">
          <span class="q-mr-md">是否启用</span>
          <q-toggle v-model="form.Enabled" dense />
        </div>
        <q-input v-model="form.Remark" outlined dense type="textarea" rows="2" label="备注" />
      </q-card-section>
      <q-card-actions align="right">
        <q-btn flat label="取消" @click="showDialog = false" />
        <q-btn color="primary" label="保存" :loading="submitting" @click="save" />
      </q-card-actions>
    </q-card>
  </q-dialog>

  <q-dialog v-model="showDelete" persistent>
    <q-card style="min-width: 320px">
      <q-card-section>
        <div class="text-h6">删除规则</div>
      </q-card-section>
      <q-card-section>确定要删除规则「{{ deleteTarget?.Name }}」（监听端口 {{ deleteTarget?.ListenPort }}）吗？删除后该监听会立即卸载。</q-card-section>
      <q-card-actions align="right">
        <q-btn flat label="取消" @click="showDelete = false" />
        <q-btn color="negative" label="删除" :loading="submitting" @click="doDelete" />
      </q-card-actions>
    </q-card>
  </q-dialog>
</div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { toast } from 'vue3-toastify'
import api from '@/axios/AxiosConfig'

interface RuleRow {
  Id: number
  Name: string
  ListenPort: number
  TargetHost: string
  TargetPort: number
  Path?: string
  Enabled: boolean
  Remark?: string
  IsListening: boolean
  StatusMessage?: string
  ActiveConnections: number
  TotalConnections: number
  TotalBytes: number
}

interface ApiResponse {
  Data?: any
  Message?: string
  Success?: boolean
}

const rows = ref<RuleRow[]>([])
const total = ref(0)
const page = ref(1)
const size = ref(10)
const keyword = ref('')
const loading = ref(false)
const submitting = ref(false)

const showDialog = ref(false)
const form = ref({ Id: 0, Name: '', ListenPort: 10000, TargetHost: '', TargetPort: 0, Path: '', Enabled: true, Remark: '' })
const showDelete = ref(false)
const deleteTarget = ref<RuleRow | null>(null)

const maxPage = computed(() => Math.max(1, Math.ceil(total.value / size.value)))

const formatBytes = (bytes: number) => {
  if (!bytes) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  const i = Math.min(units.length - 1, Math.floor(Math.log(bytes) / Math.log(1024)))
  return (bytes / Math.pow(1024, i)).toFixed(i === 0 ? 0 : 2) + ' ' + units[i]
}

const load = async () => {
  loading.value = true
  try {
    const res = await api.get('/PortForward/PortForwardPage', { params: { page: page.value, size: size.value, keyword: keyword.value } }) as ApiResponse
    rows.value = res.Data?.Items ?? []
    total.value = res.Data?.Total ?? 0
  } finally {
    loading.value = false
  }
}

const reload = () => {
  page.value = 1
  load()
}

const openCreate = () => {
  form.value = { Id: 0, Name: '', ListenPort: 10000, TargetHost: '', TargetPort: 0, Path: '', Enabled: true, Remark: '' }
  showDialog.value = true
}

const openEdit = (row: RuleRow) => {
  form.value = { Id: row.Id, Name: row.Name, ListenPort: row.ListenPort, TargetHost: row.TargetHost, TargetPort: row.TargetPort, Path: row.Path ?? '', Enabled: row.Enabled, Remark: row.Remark ?? '' }
  showDialog.value = true
}

const savePayload = async (payload: object) => {
  submitting.value = true
  try {
    const res = await api.post('/PortForward/SavePortForward', payload) as ApiResponse
    if (res.Success) {
      toast.success(res.Message || '保存成功')
      return true
    }
    return false
  } finally {
    submitting.value = false
  }
}

const save = async () => {
  if (!form.value.Name.trim()) {
    toast.error('规则名称不能为空')
    return
  }
  if (!form.value.TargetHost.trim()) {
    toast.error('目标地址不能为空')
    return
  }
  if (form.value.ListenPort < 10000 || form.value.ListenPort > 10100) {
    toast.error('监听端口只能填 10000-10100（Docker 发布端口段）')
    return
  }
  if (form.value.TargetPort < 1 || form.value.TargetPort > 65535) {
    toast.error('目标端口必须在 1-65535 之间')
    return
  }
  const path = form.value.Path.trim()
  if (path && (!path.startsWith('/') || path.length < 2 || path.endsWith('/') || path.includes(' ') || path.includes('..'))) {
    toast.error('路径必须以 / 开头、长度至少 2、不能以 / 结尾、不能包含空格或 ..（如 /mcp）')
    return
  }
  form.value.Path = path
  if (await savePayload(form.value)) {
    showDialog.value = false
    load()
  }
}

const toggleEnabled = async (row: RuleRow) => {
  const ok = await savePayload({
    Id: row.Id,
    Name: row.Name,
    ListenPort: row.ListenPort,
    TargetHost: row.TargetHost,
    TargetPort: row.TargetPort,
    Path: row.Path ?? '',
    Enabled: !row.Enabled,
    Remark: row.Remark ?? ''
  })
  if (ok) load()
}

const askDelete = (row: RuleRow) => {
  deleteTarget.value = row
  showDelete.value = true
}

const doDelete = async () => {
  if (!deleteTarget.value) return
  submitting.value = true
  try {
    const res = await api.post('/PortForward/DeletePortForward', { Id: deleteTarget.value.Id }) as ApiResponse
    if (res.Success) {
      toast.success(res.Message || '删除成功')
      showDelete.value = false
      load()
    }
  } finally {
    submitting.value = false
  }
}

onMounted(load)
</script>
