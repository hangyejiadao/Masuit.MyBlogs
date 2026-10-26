<template>
<div class="license-container">
  <div class="text-h4 q-mb-lg">License 管理</div>
  <div class="q-mb-lg">
    <div class="row items-center justify-between">
      <div class="col-auto">
        <div class="q-gutter-sm">
          <q-btn color="positive" icon="add" label="新增授权" @click="openCreate" />
          <q-btn color="primary" icon="refresh" label="刷新" @click="load" :loading="loading" />
        </div>
      </div>
      <div class="col-auto">
        <q-input v-model="keyword" outlined dense placeholder="搜索授权码、应用或用户" @keyup.enter="reload">
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
          <th class="text-left">应用</th>
          <th class="text-left">用户</th>
          <th class="text-left">提醒邮箱</th>
          <th class="text-left">授权码</th>
          <th class="text-left">方案 / 价格</th>
          <th class="text-left">生效日期</th>
          <th class="text-left">到期日期</th>
          <th class="text-center">状态</th>
          <th class="text-center">操作</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="row in rows" :key="row.Id">
          <td class="text-left">{{ row.ApplicationName }}</td>
          <td class="text-left">{{ row.UserNickName || row.UserName }}</td>
          <td class="text-left">{{ row.UserEmail || '-' }}</td>
          <td class="text-left">{{ row.LicenseKey }}</td>
          <td class="text-left">
            <div>{{ row.PlanName || '手工签发' }}</div>
            <div v-if="row.PaidPrice != null" class="text-grey">¥{{ row.PaidPrice }}</div>
          </td>
          <td class="text-left">{{ fmtDate(row.StartDate) }}</td>
          <td class="text-left">{{ fmtDate(row.ExpireDate) }}</td>
          <td class="text-center">
            <q-chip dense :color="statusColor(row)" text-color="white">{{ statusText(row) }}</q-chip>
          </td>
          <td class="text-center">
            <q-btn flat dense size="sm" color="primary" label="编辑" @click="openEdit(row)" />
            <q-btn flat dense size="sm" color="teal" label="续期" @click="openRenew(row)" />
            <q-btn flat dense size="sm" color="orange" label="发提醒" @click="askRemind(row)" />
            <q-btn flat dense size="sm" color="negative" label="删除" @click="askDelete(row)" />
          </td>
        </tr>
        <tr v-if="!loading && rows.length === 0">
          <td colspan="9" class="text-center text-grey">暂无数据</td>
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
        <div class="text-h6">{{ form.Id > 0 ? '编辑授权' : '新增授权' }}</div>
      </q-card-section>
      <q-card-section class="q-gutter-md">
        <q-select v-model="form.ApplicationInfoId" :options="appOptions" emit-value map-options outlined dense label="应用 *" @update:model-value="onAppChanged" />
        <q-select v-model="form.UserInfoId" :options="userOptions" emit-value map-options outlined dense label="被授权用户（用户管理中的普通用户，提醒发到其邮箱）*" />
        <q-select v-model="form.PricePlanId" :options="planOptions" emit-value map-options clearable outlined dense label="价格方案（可选，选后按方案定价并自动算到期日）" @update:model-value="applyPlanExpiry" />
        <div v-if="selectedPlan" class="text-grey text-caption">成交价 ¥{{ selectedPlan.Price }} 将记为快照；到期日 = 生效日 + {{ selectedPlan.ValidityDays > 0 ? selectedPlan.ValidityDays + ' 天' : '永久' }}</div>
        <q-input v-model="form.LicenseKey" outlined dense label="授权码（留空自动生成）" />
        <q-input v-model="form.StartDate" outlined dense type="date" label="生效日期 *" @update:model-value="applyPlanExpiry" />
        <q-input v-model="form.ExpireDate" outlined dense type="date" label="到期日期 *" />
        <q-input v-model="form.Remark" outlined dense type="textarea" rows="2" label="备注" />
      </q-card-section>
      <q-card-actions align="right">
        <q-btn flat label="取消" @click="showDialog = false" />
        <q-btn color="primary" label="保存" :loading="submitting" @click="save" />
      </q-card-actions>
    </q-card>
  </q-dialog>

  <q-dialog v-model="showRenew" persistent>
    <q-card style="min-width: 320px">
      <q-card-section>
        <div class="text-h6">授权续期</div>
      </q-card-section>
      <q-card-section class="q-gutter-md">
        <div>应用「{{ renewTarget?.ApplicationName }}」/ 用户「{{ renewTarget ? (renewTarget.UserNickName || renewTarget.UserName) : '' }}」，当前到期：{{ renewTarget ? fmtDate(renewTarget.ExpireDate) : '' }}</div>
        <q-input v-model.number="renewMonths" outlined dense type="number" min="1" label="续期月数" />
      </q-card-section>
      <q-card-actions align="right">
        <q-btn flat label="取消" @click="showRenew = false" />
        <q-btn color="teal" label="续期" :loading="submitting" @click="doRenew" />
      </q-card-actions>
    </q-card>
  </q-dialog>

  <q-dialog v-model="showRemind" persistent>
    <q-card style="min-width: 320px">
      <q-card-section>
        <div class="text-h6">发送到期提醒</div>
      </q-card-section>
      <q-card-section>立即给用户「{{ remindTarget ? (remindTarget.UserNickName || remindTarget.UserName) : '' }}」（{{ remindTarget?.UserEmail || '未填邮箱' }}）发送一封到期提醒邮件？</q-card-section>
      <q-card-actions align="right">
        <q-btn flat label="取消" @click="showRemind = false" />
        <q-btn color="orange" label="发送" :loading="submitting" @click="doRemind" />
      </q-card-actions>
    </q-card>
  </q-dialog>

  <q-dialog v-model="showDelete" persistent>
    <q-card style="min-width: 320px">
      <q-card-section>
        <div class="text-h6">删除授权</div>
      </q-card-section>
      <q-card-section>确定要删除授权「{{ deleteTarget?.LicenseKey }}」吗？</q-card-section>
      <q-card-actions align="right">
        <q-btn flat label="取消" @click="showDelete = false" />
        <q-btn color="negative" label="删除" :loading="submitting" @click="doDelete" />
      </q-card-actions>
    </q-card>
  </q-dialog>
</div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, nextTick } from 'vue'
import { toast } from 'vue3-toastify'
import api from '@/axios/AxiosConfig'

interface LicenseRow {
  Id: number
  ApplicationInfoId: number
  ApplicationName: string
  UserInfoId: number
  UserName: string
  UserNickName?: string
  UserEmail?: string
  PricePlanId?: number | null
  PlanName?: string | null
  PlanValidityDays?: number | null
  PaidPrice?: number | null
  LicenseKey: string
  StartDate: string
  ExpireDate: string
  Remark?: string
}

interface PlanOption {
  Id: number
  ApplicationInfoId: number
  Name: string
  Price: number
  ValidityDays: number
}

interface ApiResponse {
  Data?: any
  Message?: string
  Success?: boolean
}

const rows = ref<LicenseRow[]>([])
const total = ref(0)
const page = ref(1)
const size = ref(10)
const keyword = ref('')
const loading = ref(false)
const submitting = ref(false)

const appOptions = ref<{ label: string; value: number }[]>([])
const userOptions = ref<{ label: string; value: number }[]>([])
const allPlans = ref<PlanOption[]>([])

const showDialog = ref(false)
const form = ref({ Id: 0, ApplicationInfoId: null as number | null, UserInfoId: null as number | null, PricePlanId: null as number | null, LicenseKey: '', StartDate: '', ExpireDate: '', Remark: '' })

const planOptions = computed(() => allPlans.value.filter(p => p.ApplicationInfoId === form.value.ApplicationInfoId).map(p => ({ label: `${p.Name}（¥${p.Price} · ${p.ValidityDays > 0 ? p.ValidityDays + ' 天' : '永久'}）`, value: p.Id })))
const selectedPlan = computed(() => allPlans.value.find(p => p.Id === form.value.PricePlanId) ?? null)
const showRenew = ref(false)
const renewTarget = ref<LicenseRow | null>(null)
const renewMonths = ref(12)
const showRemind = ref(false)
const remindTarget = ref<LicenseRow | null>(null)
const showDelete = ref(false)
const deleteTarget = ref<LicenseRow | null>(null)

const maxPage = computed(() => Math.max(1, Math.ceil(total.value / size.value)))

const fmtDate = (s?: string) => (s ? s.slice(0, 10) : '')

const daysLeft = (row: LicenseRow) => {
  const expire = new Date(fmtDate(row.ExpireDate) + 'T00:00:00')
  const today = new Date()
  today.setHours(0, 0, 0, 0)
  return Math.round((expire.getTime() - today.getTime()) / 86400000)
}

const statusText = (row: LicenseRow) => {
  if (row.PlanName && row.PlanValidityDays === 0) return '永久'
  const d = daysLeft(row)
  return d < 0 ? `已过期 ${-d} 天` : d === 0 ? '今天到期' : `剩 ${d} 天`
}

const statusColor = (row: LicenseRow) => {
  if (row.PlanName && row.PlanValidityDays === 0) return 'positive'
  const d = daysLeft(row)
  return d < 0 ? 'negative' : d <= 30 ? 'orange' : 'positive'
}

const load = async () => {
  loading.value = true
  try {
    const res = await api.get('/License/LicensePage', { params: { page: page.value, size: size.value, keyword: keyword.value } }) as ApiResponse
    rows.value = res.Data?.Items ?? []
    total.value = res.Data?.Total ?? 0
  } finally {
    loading.value = false
  }
}

const loadOptions = async () => {
  const res = await api.get('/License/LicenseOptions') as ApiResponse
  if (res.Success) {
    appOptions.value = (res.Data?.Applications ?? []).map((a: any) => ({ label: a.Name, value: a.Id }))
    userOptions.value = (res.Data?.Users ?? []).map((u: any) => ({ label: `${u.NickName || u.Username}${u.Email ? `（${u.Email}）` : ''}`, value: u.Id }))
    allPlans.value = res.Data?.Plans ?? []
  }
}

const reload = () => {
  page.value = 1
  load()
}

const pad2 = (n: number) => String(n).padStart(2, '0')
const dateStr = (d: Date) => `${d.getFullYear()}-${pad2(d.getMonth() + 1)}-${pad2(d.getDate())}`

const todayStr = () => dateStr(new Date())

const onAppChanged = () => {
  form.value.PricePlanId = null
}

const applyPlanExpiry = () => {
  const plan = selectedPlan.value
  if (!plan || !form.value.StartDate) return
  const d = new Date(form.value.StartDate + 'T00:00:00')
  if (plan.ValidityDays > 0) {
    d.setDate(d.getDate() + plan.ValidityDays)
  } else {
    d.setFullYear(d.getFullYear() + 100)
  }
  form.value.ExpireDate = dateStr(d)
}

const openCreate = () => {
  const start = todayStr()
  const expireDate = new Date()
  expireDate.setFullYear(expireDate.getFullYear() + 1)
  form.value = { Id: 0, ApplicationInfoId: null, UserInfoId: null, PricePlanId: null, LicenseKey: '', StartDate: start, ExpireDate: dateStr(expireDate), Remark: '' }
  showDialog.value = true
}

const openEdit = async (row: LicenseRow) => {
  form.value = { Id: row.Id, ApplicationInfoId: row.ApplicationInfoId, UserInfoId: row.UserInfoId, PricePlanId: null, LicenseKey: row.LicenseKey, StartDate: fmtDate(row.StartDate), ExpireDate: fmtDate(row.ExpireDate), Remark: row.Remark ?? '' }
  showDialog.value = true
  await nextTick()
  form.value.PricePlanId = row.PricePlanId ?? null
}

const save = async () => {
  if (!form.value.ApplicationInfoId || !form.value.UserInfoId) {
    toast.error('请选择应用和用户')
    return
  }
  applyPlanExpiry()
  if (!form.value.StartDate || !form.value.ExpireDate) {
    toast.error('请选择生效和到期日期')
    return
  }
  submitting.value = true
  try {
    const res = await api.post('/License/SaveLicense', form.value) as ApiResponse
    if (res.Success) {
      toast.success(res.Message || '保存成功')
      showDialog.value = false
      load()
    }
  } finally {
    submitting.value = false
  }
}

const openRenew = (row: LicenseRow) => {
  renewTarget.value = row
  renewMonths.value = 12
  showRenew.value = true
}

const doRenew = async () => {
  if (!renewTarget.value) return
  submitting.value = true
  try {
    const res = await api.post('/License/RenewLicense', { Id: renewTarget.value.Id, Months: renewMonths.value }) as ApiResponse
    if (res.Success) {
      toast.success(res.Message || '续期成功')
      showRenew.value = false
      load()
    }
  } finally {
    submitting.value = false
  }
}

const askRemind = (row: LicenseRow) => {
  remindTarget.value = row
  showRemind.value = true
}

const doRemind = async () => {
  if (!remindTarget.value) return
  submitting.value = true
  try {
    const res = await api.post('/License/SendReminderNow', { Id: remindTarget.value.Id }) as ApiResponse
    if (res.Success) {
      toast.success(res.Message || '已发送')
      showRemind.value = false
    }
  } finally {
    submitting.value = false
  }
}

const askDelete = (row: LicenseRow) => {
  deleteTarget.value = row
  showDelete.value = true
}

const doDelete = async () => {
  if (!deleteTarget.value) return
  submitting.value = true
  try {
    const res = await api.post('/License/DeleteLicense', { Id: deleteTarget.value.Id }) as ApiResponse
    if (res.Success) {
      toast.success(res.Message || '删除成功')
      showDelete.value = false
      load()
    }
  } finally {
    submitting.value = false
  }
}

onMounted(() => {
  loadOptions()
  load()
})
</script>
