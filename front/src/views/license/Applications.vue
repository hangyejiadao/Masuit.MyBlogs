<template>
<div class="license-container">
  <div class="text-h4 q-mb-lg">应用管理</div>
  <div class="q-mb-lg">
    <div class="row items-center justify-between">
      <div class="col-auto">
        <div class="q-gutter-sm">
          <q-btn color="positive" icon="add" label="新增应用" @click="openCreate" />
          <q-btn color="primary" icon="refresh" label="刷新" @click="load" :loading="loading" />
        </div>
      </div>
      <div class="col-auto">
        <q-input v-model="keyword" outlined dense placeholder="搜索名称或标识" @keyup.enter="reload">
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
          <th class="text-left">应用名称</th>
          <th class="text-left">应用标识</th>
          <th class="text-left">版本</th>
          <th class="text-left">说明</th>
          <th class="text-center">授权数</th>
          <th class="text-center">价格方案</th>
          <th class="text-center">操作</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="row in rows" :key="row.Id">
          <td class="text-left">{{ row.Name }}</td>
          <td class="text-left">{{ row.AppKey }}</td>
          <td class="text-left">{{ row.Version || '-' }}</td>
          <td class="text-left">{{ row.Description || '-' }}</td>
          <td class="text-center">{{ row.LicenseCount }}</td>
          <td class="text-center">{{ row.PlanCount }}</td>
          <td class="text-center">
            <q-btn flat dense size="sm" color="teal" label="价格方案" @click="openPlans(row)" />
            <q-btn flat dense size="sm" color="primary" label="编辑" @click="openEdit(row)" />
            <q-btn flat dense size="sm" color="negative" label="删除" @click="askDelete(row)" />
          </td>
        </tr>
        <tr v-if="!loading && rows.length === 0">
          <td colspan="7" class="text-center text-grey">暂无数据</td>
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
    <q-card style="min-width: 420px">
      <q-card-section>
        <div class="text-h6">{{ form.Id > 0 ? '编辑应用' : '新增应用' }}</div>
      </q-card-section>
      <q-card-section class="q-gutter-md">
        <q-input v-model="form.Name" outlined dense label="应用名称 *" />
        <q-input v-model="form.AppKey" outlined dense label="应用标识（留空自动生成）" />
        <q-input v-model="form.Version" outlined dense label="版本号（如 1.2.0）" />
        <q-input v-model="form.PackageUrl" outlined dense label="安装包下载地址" />
        <q-input v-model="form.Description" outlined dense type="textarea" rows="3" label="说明" />
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
        <div class="text-h6">删除应用</div>
      </q-card-section>
      <q-card-section>确定要删除应用「{{ deleteTarget?.Name }}」吗？名下还有授权时将无法删除。</q-card-section>
      <q-card-actions align="right">
        <q-btn flat label="取消" @click="showDelete = false" />
        <q-btn color="negative" label="删除" :loading="submitting" @click="doDelete" />
      </q-card-actions>
    </q-card>
  </q-dialog>

  <q-dialog v-model="showPlans" persistent>
    <q-card style="min-width: 600px">
      <q-card-section>
        <div class="text-h6">价格方案 — {{ planApp?.Name }}</div>
      </q-card-section>
      <q-card-section>
        <q-markup-table flat separator="cell" dense>
          <thead>
            <tr>
              <th class="text-left">方案名称</th>
              <th class="text-right">价格（元）</th>
              <th class="text-center">有效期</th>
              <th class="text-center">排序</th>
              <th class="text-center">操作</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="p in planRows" :key="p.Id">
              <td class="text-left">{{ p.Name }}</td>
              <td class="text-right">{{ p.Price }}</td>
              <td class="text-center">{{ p.ValidityDays > 0 ? p.ValidityDays + ' 天' : '永久' }}</td>
              <td class="text-center">{{ p.Sort }}</td>
              <td class="text-center">
                <q-btn flat dense size="sm" color="primary" label="编辑" @click="editPlan(p)" />
                <q-btn flat dense size="sm" color="negative" label="删除" @click="askDeletePlan(p)" />
              </td>
            </tr>
            <tr v-if="!planLoading && planRows.length === 0">
              <td colspan="5" class="text-center text-grey">暂无方案，用下方表单新增</td>
            </tr>
          </tbody>
        </q-markup-table>
        <div class="q-mt-md q-gutter-sm">
          <div class="text-subtitle2">{{ planForm.Id > 0 ? '编辑方案' : '新增方案' }}</div>
          <div class="row q-gutter-sm">
            <q-input v-model="planForm.Name" outlined dense label="方案名称 *" class="col" />
            <q-input v-model.number="planForm.Price" outlined dense type="number" min="0" step="0.01" label="价格（元）*" class="col" />
          </div>
          <div class="row q-gutter-sm">
            <q-input v-model.number="planForm.ValidityDays" outlined dense type="number" min="0" label="有效天数（0 = 永久）*" class="col" />
            <q-input v-model.number="planForm.Sort" outlined dense type="number" label="排序" class="col" />
          </div>
          <div class="row q-gutter-sm">
            <q-btn color="primary" :label="planForm.Id > 0 ? '保存修改' : '新增方案'" :loading="planSubmitting" @click="savePlan" />
            <q-btn v-if="planForm.Id > 0" flat label="取消编辑" @click="resetPlanForm" />
          </div>
        </div>
      </q-card-section>
      <q-card-actions align="right">
        <q-btn flat label="关闭" @click="showPlans = false" />
      </q-card-actions>
    </q-card>
  </q-dialog>

  <q-dialog v-model="showPlanDelete" persistent>
    <q-card style="min-width: 320px">
      <q-card-section>
        <div class="text-h6">删除价格方案</div>
      </q-card-section>
      <q-card-section>确定要删除方案「{{ planDeleteTarget?.Name }}」吗？已有授权在用该方案时将无法删除。</q-card-section>
      <q-card-actions align="right">
        <q-btn flat label="取消" @click="showPlanDelete = false" />
        <q-btn color="negative" label="删除" :loading="planSubmitting" @click="doDeletePlan" />
      </q-card-actions>
    </q-card>
  </q-dialog>
</div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { toast } from 'vue3-toastify'
import api from '@/axios/AxiosConfig'

interface AppRow {
  Id: number
  Name: string
  AppKey: string
  Description?: string
  Version?: string
  PackageUrl?: string
  LicenseCount: number
  PlanCount: number
}

interface PlanRow {
  Id: number
  ApplicationInfoId: number
  Name: string
  Price: number
  ValidityDays: number
  Sort: number
}

interface ApiResponse {
  Data?: any
  Message?: string
  Success?: boolean
}

const rows = ref<AppRow[]>([])
const total = ref(0)
const page = ref(1)
const size = ref(10)
const keyword = ref('')
const loading = ref(false)
const submitting = ref(false)

const showDialog = ref(false)
const form = ref({ Id: 0, Name: '', AppKey: '', Description: '', Version: '', PackageUrl: '' })
const showDelete = ref(false)
const deleteTarget = ref<AppRow | null>(null)

const showPlans = ref(false)
const planApp = ref<AppRow | null>(null)
const planRows = ref<PlanRow[]>([])
const planLoading = ref(false)
const planSubmitting = ref(false)
const planForm = ref({ Id: 0, Name: '', Price: 0, ValidityDays: 365, Sort: 0 })
const showPlanDelete = ref(false)
const planDeleteTarget = ref<PlanRow | null>(null)

const maxPage = computed(() => Math.max(1, Math.ceil(total.value / size.value)))

const load = async () => {
  loading.value = true
  try {
    const res = await api.get('/License/ApplicationPage', { params: { page: page.value, size: size.value, keyword: keyword.value } }) as ApiResponse
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
  form.value = { Id: 0, Name: '', AppKey: '', Description: '', Version: '', PackageUrl: '' }
  showDialog.value = true
}

const openEdit = (row: AppRow) => {
  form.value = { Id: row.Id, Name: row.Name, AppKey: row.AppKey, Description: row.Description ?? '', Version: row.Version ?? '', PackageUrl: row.PackageUrl ?? '' }
  showDialog.value = true
}

const save = async () => {
  if (!form.value.Name.trim()) {
    toast.error('应用名称不能为空')
    return
  }
  submitting.value = true
  try {
    const res = await api.post('/License/SaveApplication', form.value) as ApiResponse
    if (res.Success) {
      toast.success(res.Message || '保存成功')
      showDialog.value = false
      load()
    }
  } finally {
    submitting.value = false
  }
}

const askDelete = (row: AppRow) => {
  deleteTarget.value = row
  showDelete.value = true
}

const doDelete = async () => {
  if (!deleteTarget.value) return
  submitting.value = true
  try {
    const res = await api.post('/License/DeleteApplication', { Id: deleteTarget.value.Id }) as ApiResponse
    if (res.Success) {
      toast.success(res.Message || '删除成功')
      showDelete.value = false
      load()
    }
  } finally {
    submitting.value = false
  }
}

const resetPlanForm = () => {
  planForm.value = { Id: 0, Name: '', Price: 0, ValidityDays: 365, Sort: 0 }
}

const loadPlans = async () => {
  if (!planApp.value) return
  planLoading.value = true
  try {
    const res = await api.get('/License/PricePlanList', { params: { applicationId: planApp.value.Id } }) as ApiResponse
    planRows.value = res.Data ?? []
  } finally {
    planLoading.value = false
  }
}

const openPlans = (row: AppRow) => {
  planApp.value = row
  resetPlanForm()
  showPlans.value = true
  loadPlans()
}

const editPlan = (p: PlanRow) => {
  planForm.value = { Id: p.Id, Name: p.Name, Price: p.Price, ValidityDays: p.ValidityDays, Sort: p.Sort }
}

const savePlan = async () => {
  if (!planApp.value) return
  if (!planForm.value.Name.trim()) {
    toast.error('方案名称不能为空')
    return
  }
  if (planForm.value.Price < 0 || planForm.value.ValidityDays < 0) {
    toast.error('价格和有效天数不能为负数')
    return
  }
  planSubmitting.value = true
  try {
    const res = await api.post('/License/SavePricePlan', { ...planForm.value, ApplicationInfoId: planApp.value.Id }) as ApiResponse
    if (res.Success) {
      toast.success(res.Message || '保存成功')
      resetPlanForm()
      loadPlans()
      load()
    }
  } finally {
    planSubmitting.value = false
  }
}

const askDeletePlan = (p: PlanRow) => {
  planDeleteTarget.value = p
  showPlanDelete.value = true
}

const doDeletePlan = async () => {
  if (!planDeleteTarget.value) return
  planSubmitting.value = true
  try {
    const res = await api.post('/License/DeletePricePlan', { Id: planDeleteTarget.value.Id }) as ApiResponse
    if (res.Success) {
      toast.success(res.Message || '删除成功')
      showPlanDelete.value = false
      if (planForm.value.Id === planDeleteTarget.value.Id) resetPlanForm()
      loadPlans()
      load()
    }
  } finally {
    planSubmitting.value = false
  }
}

onMounted(load)
</script>
