<template>
<div class="portforward-container">
  <div class="text-h4 q-mb-lg">端口转发</div>
  <div class="q-mb-lg">
    <div class="row items-center justify-between">
      <div class="col-auto"><div class="q-gutter-sm"><q-btn color="positive" icon="add" label="新增规则" @click="openCreate" /><q-btn color="primary" icon="refresh" label="刷新" @click="load" :loading="loading" /></div></div>
      <div class="col-auto"><q-input v-model="keyword" outlined dense placeholder="搜索名称或目标" @keyup.enter="reload"><template v-slot:prepend><q-icon name="search" /></template></q-input></div>
    </div>
  </div>

  <q-card>
    <q-markup-table flat separator="cell">
      <thead><tr><th class="text-left">名称</th><th class="text-center">模式</th><th class="text-center">监听端口</th><th class="text-left">目标</th><th class="text-left">路径</th><th class="text-center">启用</th><th class="text-center">监听状态</th><th class="text-center">活跃连接</th><th class="text-right">累计流量</th><th class="text-left">备注</th><th class="text-center">操作</th></tr></thead>
      <tbody>
        <tr v-for="row in rows" :key="row.Id">
          <td class="text-left">{{ row.Name }}</td>
          <td class="text-center"><q-badge :color="row.Mode === 1 ? 'deep-orange' : 'blue-grey'" :label="row.Mode === 1 ? '内网穿透' : '普通直连'" /></td>
          <td class="text-center">{{ row.ListenPort }}</td>
          <td class="text-left">{{ row.Mode === 1 ? `客户端 ${row.TunnelId}` : `${row.TargetHost}:${row.TargetPort}` }}</td>
          <td class="text-left">{{ row.Path || '-' }}</td>
          <td class="text-center"><q-toggle :model-value="row.Enabled" dense @update:model-value="toggleEnabled(row)" /></td>
          <td class="text-center"><q-badge v-if="!row.Enabled" color="grey" label="已停用" /><q-badge v-else-if="row.IsListening" color="positive" label="监听中" /><q-badge v-else color="negative" :label="row.StatusMessage || '未启动'"><q-tooltip v-if="row.StatusMessage">{{ row.StatusMessage }}</q-tooltip></q-badge></td>
          <td class="text-center">{{ row.ActiveConnections }}</td>
          <td class="text-right">{{ formatBytes(row.TotalBytes) }}</td>
          <td class="text-left">{{ row.Remark || '-' }}</td>
          <td class="text-center"><q-btn flat dense size="sm" color="primary" icon="history" label="请求记录" @click="openRequests(row)" /><q-btn flat dense size="sm" color="primary" label="编辑" @click="openEdit(row)" /><q-btn flat dense size="sm" color="negative" label="删除" @click="askDelete(row)" /></td>
        </tr>
        <tr v-if="!loading && rows.length === 0"><td colspan="11" class="text-center text-grey">暂无数据</td></tr>
      </tbody>
    </q-markup-table>
    <div class="row items-center justify-between q-pa-md"><div class="row items-center q-gutter-sm"><span class="text-grey">共 {{ total }} 条</span><q-select v-model="size" :options="[10, 20, 50]" dense outlined label="每页" @update:model-value="reload" /></div><q-pagination v-model="page" :max="maxPage" boundary-numbers direction-links @update:model-value="load" /></div>
  </q-card>

  <q-dialog v-model="showDialog" persistent>
    <q-card style="min-width: 460px">
      <q-card-section><div class="text-h6">{{ form.Id > 0 ? '编辑规则' : '新增规则' }}</div></q-card-section>
      <q-card-section class="q-gutter-md">
        <q-input v-model="form.Name" outlined dense label="规则名称 *" />
        <q-select v-model="form.Mode" :options="modeOptions" outlined dense emit-value map-options label="转发模式 *" />
        <q-input v-model.number="form.ListenPort" outlined dense type="number" min="10000" max="10100" label="监听端口 *" hint="只能填 10000-10100，外部端口需要在 Docker 中发布" />
        <template v-if="form.Mode === 0">
          <q-input v-model="form.TargetHost" outlined dense label="目标地址 *（IP 或主机名）" />
          <q-input v-model.number="form.TargetPort" outlined dense type="number" min="1" max="65535" label="目标端口 *" />
          <q-input v-model="form.Path" outlined dense label="路径（可选，如 /mcp）" hint="填写后，访问博客该路径的 HTTP 请求会代理到目标；留空则只提供 TCP 端口转发" />
        </template>
        <template v-else>
          <q-input v-model="form.TunnelId" outlined dense label="隧道编号 *" hint="只能包含字母、数字、连字符和下划线" />
          <q-input v-model="form.TunnelToken" outlined dense type="password" label="服务端密钥 *" :hint="form.Id > 0 ? '编辑时留空表示沿用已有密钥；填写新密钥会立即使旧客户端失效' : '新建时必须填写，长度 16-256 个字符'" />
        </template>
        <div class="row items-center"><span class="q-mr-md">是否启用</span><q-toggle v-model="form.Enabled" dense /></div>
        <q-input v-model="form.Remark" outlined dense type="textarea" rows="2" label="备注" />
      </q-card-section>
      <q-card-actions align="right"><q-btn flat label="取消" @click="showDialog = false" /><q-btn color="primary" label="保存" :loading="submitting" @click="save" /></q-card-actions>
    </q-card>
  </q-dialog>

  <q-dialog v-model="showDelete" persistent><q-card style="min-width: 320px"><q-card-section><div class="text-h6">删除规则</div></q-card-section><q-card-section>确定要删除规则「{{ deleteTarget?.Name }}」（监听端口 {{ deleteTarget?.ListenPort }}）吗？删除后该监听会立即卸载。</q-card-section><q-card-actions align="right"><q-btn flat label="取消" @click="showDelete = false" /><q-btn color="negative" label="删除" :loading="submitting" @click="doDelete" /></q-card-actions></q-card></q-dialog>

  <q-dialog v-model="showRequests">
    <q-card style="width: min(1100px, 95vw); max-width: 1100px">
      <q-card-section class="row items-center justify-between">
        <div>
          <div class="text-h6">请求记录</div>
          <div class="text-caption text-grey">{{ requestRule?.Name }} · 监听端口 {{ requestRule?.ListenPort }} · 最多保留 500 条，当前显示最近 100 条，服务重启后清空</div>
        </div>
        <q-btn flat round dense icon="refresh" :loading="requestsLoading" @click="requestRule && loadRequests(requestRule.Id)"><q-tooltip>刷新记录</q-tooltip></q-btn>
      </q-card-section>
      <q-separator />
      <q-card-section class="q-pa-none">
        <q-markup-table flat separator="cell" wrap-cells>
          <thead><tr><th class="text-left">访问时间</th><th class="text-left">来源 IP</th><th class="text-right">来源端口</th><th class="text-right">监听端口</th><th class="text-left">隧道编号</th><th class="text-center">状态</th><th class="text-right">耗时</th><th class="text-right">流量</th><th class="text-left">错误</th></tr></thead>
          <tbody>
            <tr v-for="item in requestRows" :key="item.Id">
              <td class="text-left">{{ formatDate(item.StartedAt) }}</td>
              <td class="text-left"><code>{{ item.RemoteIp }}</code></td>
              <td class="text-right">{{ item.RemotePort ?? '-' }}</td>
              <td class="text-right">{{ item.ListenPort }}</td>
              <td class="text-left">{{ item.TunnelId || '-' }}</td>
              <td class="text-center"><q-badge :color="requestStatusColor(item.Status)" :label="item.Status" /></td>
              <td class="text-right">{{ formatDuration(item.DurationMs) }}</td>
              <td class="text-right">{{ formatBytes(item.Bytes) }}</td>
              <td class="text-left"><span v-if="item.Error" class="text-negative">{{ item.Error }}</span><span v-else>-</span></td>
            </tr>
            <tr v-if="!requestsLoading && requestRows.length === 0"><td colspan="9" class="text-center text-grey q-pa-lg">暂无访问记录</td></tr>
          </tbody>
        </q-markup-table>
      </q-card-section>
      <q-card-actions align="right"><q-btn flat label="关闭" @click="showRequests = false" /></q-card-actions>
    </q-card>
  </q-dialog>
</div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { toast } from 'vue3-toastify'
import dayjs from 'dayjs'
import api from '@/axios/AxiosConfig'

interface RuleRow {
  Id: number
  Name: string
  Mode: number
  ListenPort: number
  TargetHost: string
  TargetPort: number
  Path?: string
  TunnelId?: string
  TunnelTokenConfigured?: boolean
  Enabled: boolean
  Remark?: string
  IsListening: boolean
  StatusMessage?: string
  ActiveConnections: number
  TotalConnections: number
  TotalBytes: number
}
interface ApiResponse { Data?: any; Message?: string; Success?: boolean }
interface RequestRow {
  Id: number
  RuleId: number
  RuleName: string
  Mode: number
  ListenPort: number
  TunnelId?: string
  RemoteIp: string
  RemotePort?: number
  StartedAt: string
  CompletedAt?: string
  DurationMs: number
  Status: string
  Error?: string
  Bytes: number
}

const modeOptions = [{ label: '普通直连', value: 0 }, { label: '内网穿透（客户端主动连接）', value: 1 }]
const rows = ref<RuleRow[]>([])
const total = ref(0)
const page = ref(1)
const size = ref(10)
const keyword = ref('')
const loading = ref(false)
const submitting = ref(false)
const showDialog = ref(false)
const form = ref({ Id: 0, Name: '', Mode: 0, ListenPort: 10000, TargetHost: '', TargetPort: 0, Path: '', TunnelId: '', TunnelToken: '', Enabled: true, Remark: '' })
const showDelete = ref(false)
const deleteTarget = ref<RuleRow | null>(null)
const showRequests = ref(false)
const requestsLoading = ref(false)
const requestRule = ref<RuleRow | null>(null)
const requestRows = ref<RequestRow[]>([])
const maxPage = computed(() => Math.max(1, Math.ceil(total.value / size.value)))
const formatBytes = (bytes: number) => { if (!bytes) return '0 B'; const units = ['B', 'KB', 'MB', 'GB', 'TB']; const i = Math.min(units.length - 1, Math.floor(Math.log(bytes) / Math.log(1024))); return (bytes / Math.pow(1024, i)).toFixed(i === 0 ? 0 : 2) + ' ' + units[i] }
const formatDate = (value: string) => value ? dayjs(value).format('YYYY-MM-DD HH:mm:ss') : '-'
const formatDuration = (milliseconds: number) => milliseconds < 1000 ? `${milliseconds} ms` : `${(milliseconds / 1000).toFixed(2)} s`
const requestStatusColor = (status: string) => status === '已完成' ? 'positive' : status === '处理中' ? 'info' : status === '已取消' ? 'grey' : 'negative'
const load = async () => { loading.value = true; try { const res = await api.get('/PortForward/PortForwardPage', { params: { page: page.value, size: size.value, keyword: keyword.value } }) as ApiResponse; rows.value = res.Data?.Items ?? []; total.value = res.Data?.Total ?? 0 } finally { loading.value = false } }
const reload = () => { page.value = 1; load() }
const openCreate = () => { form.value = { Id: 0, Name: '', Mode: 0, ListenPort: 10000, TargetHost: '', TargetPort: 0, Path: '', TunnelId: '', TunnelToken: '', Enabled: true, Remark: '' }; showDialog.value = true }
const openEdit = (row: RuleRow) => { form.value = { Id: row.Id, Name: row.Name, Mode: row.Mode, ListenPort: row.ListenPort, TargetHost: row.TargetHost, TargetPort: row.TargetPort, Path: row.Path ?? '', TunnelId: row.TunnelId ?? '', TunnelToken: '', Enabled: row.Enabled, Remark: row.Remark ?? '' }; showDialog.value = true }
const savePayload = async (payload: object) => { submitting.value = true; try { const res = await api.post('/PortForward/SavePortForward', payload) as ApiResponse; if (res.Success) { toast.success(res.Message || '保存成功'); return true } toast.error(res.Message || '保存失败'); return false } finally { submitting.value = false } }
const save = async () => {
  if (!form.value.Name.trim()) { toast.error('规则名称不能为空'); return }
  if (form.value.ListenPort < 10000 || form.value.ListenPort > 10100) { toast.error('监听端口只能填 10000-10100'); return }
  if (form.value.Mode === 0) {
    if (!form.value.TargetHost.trim()) { toast.error('普通直连模式的目标地址不能为空'); return }
    if (form.value.TargetPort < 1 || form.value.TargetPort > 65535) { toast.error('目标端口必须在 1-65535 之间'); return }
    const path = form.value.Path.trim(); if (path && (!path.startsWith('/') || path.length < 2 || path.length > 128 || path.endsWith('/') || path.includes(' ') || path.includes('..'))) { toast.error('路径格式不正确'); return }; form.value.Path = path
  } else {
    if (!form.value.TunnelId.trim() || !/^[A-Za-z0-9_-]+$/.test(form.value.TunnelId.trim())) { toast.error('隧道编号格式不正确'); return }
    if (form.value.Id === 0 && form.value.TunnelToken.trim().length < 16) { toast.error('新建内网穿透规则必须填写至少 16 个字符的密钥'); return }
    if (form.value.TunnelToken.trim() && (form.value.TunnelToken.trim().length < 16 || form.value.TunnelToken.trim().length > 256)) { toast.error('密钥长度必须在 16-256 个字符之间'); return }
    form.value.TargetHost = ''; form.value.TargetPort = 0; form.value.Path = ''
  }
  if (await savePayload(form.value)) { showDialog.value = false; load() }
}
const toggleEnabled = async (row: RuleRow) => { const ok = await savePayload({ Id: row.Id, Name: row.Name, Mode: row.Mode, ListenPort: row.ListenPort, TargetHost: row.TargetHost, TargetPort: row.TargetPort, Path: row.Path ?? '', TunnelId: row.TunnelId ?? '', TunnelToken: '', Enabled: !row.Enabled, Remark: row.Remark ?? '' }); if (ok) load() }
const askDelete = (row: RuleRow) => { deleteTarget.value = row; showDelete.value = true }
const openRequests = (row: RuleRow) => { requestRule.value = row; requestRows.value = []; showRequests.value = true; loadRequests(row.Id) }
const loadRequests = async (ruleId: number) => { requestsLoading.value = true; try { const res = await api.get('/PortForward/RecentRequests', { params: { ruleId, limit: 100 } }) as ApiResponse; if (requestRule.value?.Id === ruleId) requestRows.value = res.Data?.Items ?? [] } finally { requestsLoading.value = false } }
const doDelete = async () => { if (!deleteTarget.value) return; submitting.value = true; try { const res = await api.post('/PortForward/DeletePortForward', { Id: deleteTarget.value.Id }) as ApiResponse; if (res.Success) { toast.success(res.Message || '删除成功'); showDelete.value = false; load() } else toast.error(res.Message || '删除失败') } finally { submitting.value = false } }
onMounted(load)
</script>
