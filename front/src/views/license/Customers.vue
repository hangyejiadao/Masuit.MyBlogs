<template>
<div class="license-container">
  <div class="text-h4 q-mb-lg">客户管理</div>
  <div class="q-mb-lg">
    <div class="row items-center justify-between">
      <div class="col-auto">
        <div class="q-gutter-sm">
          <q-btn color="positive" icon="add" label="新增客户" @click="openCreate" />
          <q-btn color="primary" icon="refresh" label="刷新" @click="load" :loading="loading" />
        </div>
      </div>
      <div class="col-auto">
        <q-input v-model="keyword" outlined dense placeholder="搜索名称、联系人或邮箱" @keyup.enter="reload">
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
          <th class="text-left">客户名称</th>
          <th class="text-left">联系人</th>
          <th class="text-left">邮箱（接收到期提醒）</th>
          <th class="text-left">电话</th>
          <th class="text-left">备注</th>
          <th class="text-center">授权数</th>
          <th class="text-center">操作</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="row in rows" :key="row.Id">
          <td class="text-left">{{ row.Name }}</td>
          <td class="text-left">{{ row.ContactName || '-' }}</td>
          <td class="text-left">{{ row.Email || '-' }}</td>
          <td class="text-left">{{ row.Phone || '-' }}</td>
          <td class="text-left">{{ row.Remark || '-' }}</td>
          <td class="text-center">{{ row.LicenseCount }}</td>
          <td class="text-center">
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
        <div class="text-h6">{{ form.Id > 0 ? '编辑客户' : '新增客户' }}</div>
      </q-card-section>
      <q-card-section class="q-gutter-md">
        <q-input v-model="form.Name" outlined dense label="客户名称 *" />
        <q-input v-model="form.ContactName" outlined dense label="联系人" />
        <q-input v-model="form.Email" outlined dense label="邮箱（授权到期提醒发到这里）" />
        <q-input v-model="form.Phone" outlined dense label="电话" />
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
        <div class="text-h6">删除客户</div>
      </q-card-section>
      <q-card-section>确定要删除客户「{{ deleteTarget?.Name }}」吗？名下还有授权时将无法删除。</q-card-section>
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

interface CustomerRow {
  Id: number
  Name: string
  ContactName?: string
  Email?: string
  Phone?: string
  Remark?: string
  LicenseCount: number
}

interface ApiResponse {
  Data?: any
  Message?: string
  Success?: boolean
}

const rows = ref<CustomerRow[]>([])
const total = ref(0)
const page = ref(1)
const size = ref(10)
const keyword = ref('')
const loading = ref(false)
const submitting = ref(false)

const showDialog = ref(false)
const form = ref({ Id: 0, Name: '', ContactName: '', Email: '', Phone: '', Remark: '' })
const showDelete = ref(false)
const deleteTarget = ref<CustomerRow | null>(null)

const maxPage = computed(() => Math.max(1, Math.ceil(total.value / size.value)))

const load = async () => {
  loading.value = true
  try {
    const res = await api.get('/License/CustomerPage', { params: { page: page.value, size: size.value, keyword: keyword.value } }) as ApiResponse
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
  form.value = { Id: 0, Name: '', ContactName: '', Email: '', Phone: '', Remark: '' }
  showDialog.value = true
}

const openEdit = (row: CustomerRow) => {
  form.value = { Id: row.Id, Name: row.Name, ContactName: row.ContactName ?? '', Email: row.Email ?? '', Phone: row.Phone ?? '', Remark: row.Remark ?? '' }
  showDialog.value = true
}

const save = async () => {
  if (!form.value.Name.trim()) {
    toast.error('客户名称不能为空')
    return
  }
  submitting.value = true
  try {
    const res = await api.post('/License/SaveCustomer', form.value) as ApiResponse
    if (res.Success) {
      toast.success(res.Message || '保存成功')
      showDialog.value = false
      load()
    }
  } finally {
    submitting.value = false
  }
}

const askDelete = (row: CustomerRow) => {
  deleteTarget.value = row
  showDelete.value = true
}

const doDelete = async () => {
  if (!deleteTarget.value) return
  submitting.value = true
  try {
    const res = await api.post('/License/DeleteCustomer', { Id: deleteTarget.value.Id }) as ApiResponse
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
