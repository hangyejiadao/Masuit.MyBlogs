<template>
<div class="license-container">
  <div class="text-h4 q-mb-lg">订单管理</div>
  <div class="q-mb-lg">
    <div class="row items-center justify-between">
      <div class="col-auto">
        <div class="q-gutter-sm">
          <q-btn color="primary" icon="refresh" label="刷新" @click="load" :loading="loading" />
        </div>
      </div>
      <div class="col-auto">
        <div class="row items-center q-gutter-sm">
          <q-select v-model="statusFilter" :options="statusOptions" option-value="value" option-label="label" emit-value map-options outlined dense label="订单状态" style="min-width: 140px" @update:model-value="reload" />
          <q-input v-model="keyword" outlined dense placeholder="搜索订单号/用户/应用/交易号" @keyup.enter="reload">
            <template v-slot:prepend>
              <q-icon name="search" />
            </template>
          </q-input>
        </div>
      </div>
    </div>
  </div>

  <q-card>
    <q-markup-table flat separator="cell">
      <thead>
        <tr>
          <th class="text-left">订单号</th>
          <th class="text-left">用户</th>
          <th class="text-left">应用</th>
          <th class="text-left">方案</th>
          <th class="text-right">金额（元）</th>
          <th class="text-center">状态</th>
          <th class="text-left">创建时间</th>
          <th class="text-left">支付时间</th>
          <th class="text-center">操作</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="row in rows" :key="row.Id">
          <td class="text-left">{{ row.OrderNo }}</td>
          <td class="text-left">{{ row.UserNickName || row.UserName }}</td>
          <td class="text-left">{{ row.ApplicationName }}</td>
          <td class="text-left">{{ row.PlanName }}</td>
          <td class="text-right">{{ row.Amount }}</td>
          <td class="text-center">
            <q-badge :color="statusColor(row.OrderStatus)">{{ row.OrderStatusText }}</q-badge>
          </td>
          <td class="text-left">{{ fmtDateTime(row.CreatedAt) }}</td>
          <td class="text-left">{{ row.PaidAt ? fmtDateTime(row.PaidAt) : '-' }}</td>
          <td class="text-center">
            <q-btn flat class="order-action-btn" color="primary" label="详情" @click="openDetail(row)" />
            <q-btn v-if="row.OrderStatus === 0" flat class="order-action-btn" color="negative" label="关闭订单" @click="askClose(row)" />
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

  <q-dialog v-model="showDetail" persistent>
    <q-card style="min-width: 560px">
      <q-card-section>
        <div class="text-h6">订单详情 — {{ detail?.OrderNo }}</div>
      </q-card-section>
      <q-card-section v-if="detail">
        <q-markup-table flat separator="cell" dense>
          <tbody>
            <tr><td class="text-grey" style="width: 130px">订单号</td><td>{{ detail.OrderNo }}</td></tr>
            <tr><td class="text-grey">用户</td><td>{{ detail.UserNickName || detail.UserName }}（{{ detail.UserName }} / {{ detail.UserEmail || '无邮箱' }}）</td></tr>
            <tr><td class="text-grey">应用</td><td>{{ detail.ApplicationName }}（{{ detail.ApplicationAppKey }}）</td></tr>
            <tr><td class="text-grey">价格方案</td><td>{{ detail.PlanName }} · ¥{{ detail.PlanPrice }} · 有效期 {{ detail.PlanValidityDays > 0 ? detail.PlanValidityDays + ' 天' : '永久' }}</td></tr>
            <tr><td class="text-grey">应付金额</td><td>¥{{ detail.Amount }}</td></tr>
            <tr><td class="text-grey">订单状态</td><td><q-badge :color="statusColor(detail.OrderStatus)">{{ detail.OrderStatusText }}</q-badge></td></tr>
            <tr><td class="text-grey">创建时间</td><td>{{ fmtDateTime(detail.CreatedAt) }}</td></tr>
            <tr><td class="text-grey">支付时间</td><td>{{ detail.PaidAt ? fmtDateTime(detail.PaidAt) : '-' }}</td></tr>
            <tr><td class="text-grey">支付宝交易号</td><td>{{ detail.AlipayTradeNo || '-' }}</td></tr>
            <tr>
              <td class="text-grey">发放的授权</td>
              <td v-if="detail.IssuedLicenseId">
                <div style="word-break: break-all">{{ detail.IssuedLicenseKey }}</div>
                <div class="text-grey">生效 {{ fmtDate(detail.IssuedLicenseStartDate) }} · 到期 {{ expireText(detail) }}</div>
              </td>
              <td v-else>-</td>
            </tr>
          </tbody>
        </q-markup-table>
      </q-card-section>
      <q-card-actions align="right">
        <q-btn flat label="关闭" @click="showDetail = false" />
      </q-card-actions>
    </q-card>
  </q-dialog>

  <q-dialog v-model="showClose" persistent>
    <q-card style="min-width: 320px">
      <q-card-section>
        <div class="text-h6">关闭订单</div>
      </q-card-section>
      <q-card-section>确定要关闭订单「{{ closeTarget?.OrderNo }}」吗？仅待支付订单可关闭，关闭后用户将无法继续支付该订单（可重新下单）。</q-card-section>
      <q-card-actions align="right">
        <q-btn flat label="取消" @click="showClose = false" />
        <q-btn color="negative" label="关闭订单" :loading="submitting" @click="doClose" />
      </q-card-actions>
    </q-card>
  </q-dialog>
</div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { toast } from 'vue3-toastify'
import api from '@/axios/AxiosConfig'

interface OrderRow {
  Id: number
  OrderNo: string
  UserInfoId: number
  UserName: string
  UserNickName?: string
  UserEmail?: string
  ApplicationInfoId: number
  ApplicationName: string
  PricePlanId: number
  PlanName: string
  PlanPrice: number
  PlanValidityDays: number
  Amount: number
  OrderStatus: number
  OrderStatusText: string
  CreatedAt: string
  PaidAt?: string
  AlipayTradeNo?: string
  IssuedLicenseId?: number
  IssuedLicenseKey?: string
  IssuedLicenseExpireDate?: string
}

interface OrderDetail extends OrderRow {
  ApplicationAppKey: string
  IssuedLicenseStartDate?: string
}

interface ApiResponse {
  Data?: any
  Message?: string
  Success?: boolean
}

const rows = ref<OrderRow[]>([])
const total = ref(0)
const page = ref(1)
const size = ref(10)
const keyword = ref('')
const statusFilter = ref<number | null>(null)
const loading = ref(false)
const submitting = ref(false)

const statusOptions = [
  { label: '全部状态', value: null },
  { label: '待支付', value: 0 },
  { label: '已支付', value: 1 },
  { label: '已关闭', value: 2 },
]

const showDetail = ref(false)
const detail = ref<OrderDetail | null>(null)
const showClose = ref(false)
const closeTarget = ref<OrderRow | null>(null)

const maxPage = computed(() => Math.max(1, Math.ceil(total.value / size.value)))

const fmtDateTime = (s?: string) => (s ? s.slice(0, 19).replace('T', ' ') : '')
const fmtDate = (s?: string) => (s ? s.slice(0, 10) : '')
const expireText = (d: OrderDetail) => {
  if (!d.IssuedLicenseExpireDate || !d.IssuedLicenseStartDate) return ''
  const start = new Date(fmtDate(d.IssuedLicenseStartDate) + 'T00:00:00')
  const expire = new Date(fmtDate(d.IssuedLicenseExpireDate) + 'T00:00:00')
  const years = (expire.getTime() - start.getTime()) / (365.25 * 24 * 3600 * 1000)
  return years >= 99 ? '永久' : fmtDate(d.IssuedLicenseExpireDate)
}
const statusColor = (s: number) => (s === 1 ? 'positive' : s === 2 ? 'grey' : 'orange')

const load = async () => {
  loading.value = true
  try {
    const params: Record<string, any> = { page: page.value, size: size.value, keyword: keyword.value }
    if (statusFilter.value !== null) params.status = statusFilter.value
    const res = await api.get('/StoreOrder/OrderPage', { params }) as ApiResponse
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

const openDetail = async (row: OrderRow) => {
  const res = await api.get('/StoreOrder/OrderDetail', { params: { id: row.Id } }) as ApiResponse
  if (res.Success) {
    detail.value = res.Data as OrderDetail
    showDetail.value = true
  }
}

const askClose = (row: OrderRow) => {
  closeTarget.value = row
  showClose.value = true
}

const doClose = async () => {
  if (!closeTarget.value) return
  submitting.value = true
  try {
    const res = await api.post('/StoreOrder/CloseOrder', { Id: closeTarget.value.Id }) as ApiResponse
    if (res.Success) {
      toast.success(res.Message || '订单已关闭')
      showClose.value = false
      load()
    }
  } finally {
    submitting.value = false
  }
}

onMounted(load)
</script>

<style scoped>
.order-action-btn {
  font-size: 15px;
}
</style>
