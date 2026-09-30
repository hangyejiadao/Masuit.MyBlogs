<template>
  <div class="system-logs-page">
    <div class="row items-center q-mb-md">
      <div class="text-h4">HTTP 请求日志</div>
      <q-space />
      <q-toggle v-model="errorsOnly" label="仅显示异常" color="negative" />
      <q-btn flat round icon="refresh" :loading="loading" aria-label="刷新" @click="loadLogs(pagination)" />
    </div>

    <q-card>
      <q-table
        v-model:pagination="pagination"
        :rows="logs"
        :columns="columns"
        :loading="loading"
        row-key="Id"
        :rows-per-page-options="[20, 50, 100]"
        @request="onRequest"
      >
        <template #body-cell-result="props">
          <q-td :props="props">
            <q-badge :color="props.row.IsException ? 'negative' : 'positive'">
              {{ props.row.IsException ? '异常' : '正常' }}
            </q-badge>
            <span class="q-ml-sm">{{ props.row.StatusCode }}</span>
          </q-td>
        </template>

        <template #body-cell-actions="props">
          <q-td :props="props">
            <q-btn flat dense color="primary" label="详情" @click="selectedLog = props.row; showDetails = true" />
          </q-td>
        </template>

        <template #no-data>
          <div class="full-width row flex-center q-gutter-sm q-pa-lg text-grey-7">
            {{ loading ? '正在加载请求日志…' : '暂无请求日志' }}
          </div>
        </template>
      </q-table>
    </q-card>

    <q-dialog v-model="showDetails" maximized>
      <q-card v-if="selectedLog">
        <q-card-section class="row items-center">
          <div class="text-h6">请求详情</div>
          <q-space />
          <q-btn flat round dense icon="close" aria-label="关闭" v-close-popup />
        </q-card-section>
        <q-separator />
        <q-card-section class="detail-content">
          <div class="detail-grid">
            <div><strong>时间：</strong>{{ formatTime(selectedLog.Time) }}</div>
            <div><strong>结果：</strong>{{ selectedLog.IsException ? '异常' : '正常' }}（HTTP {{ selectedLog.StatusCode }}）</div>
            <div><strong>方式：</strong>{{ selectedLog.Method }}</div>
            <div><strong>耗时：</strong>{{ selectedLog.DurationMilliseconds }} ms</div>
            <div class="full-row"><strong>路径：</strong>{{ selectedLog.Path }}</div>
            <div><strong>客户端 IP：</strong>{{ selectedLog.IP || '-' }}</div>
            <div><strong>跟踪 ID：</strong>{{ selectedLog.TraceId }}</div>
            <div class="full-row"><strong>User-Agent：</strong>{{ selectedLog.UserAgent || '-' }}</div>
          </div>

          <h6>请求参数</h6>
          <pre>{{ prettyJson(selectedLog.RequestParameters) }}</pre>
          <h6>请求结果</h6>
          <pre>{{ prettyJson(selectedLog.ResponseResult) }}</pre>
          <template v-if="selectedLog.ExceptionInfo">
            <h6 class="text-negative">异常信息</h6>
            <pre class="exception-info">{{ selectedLog.ExceptionInfo }}</pre>
          </template>
        </q-card-section>
      </q-card>
    </q-dialog>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { toast } from 'vue3-toastify'
import api from '../../axios/AxiosConfig'

interface HttpRequestLog {
  Id: number
  Time: string
  Method: string
  Path: string
  RequestParameters: string
  ResponseResult: string
  StatusCode: number
  ExceptionInfo: string | null
  IsException: boolean
  IP: string | null
  UserAgent: string | null
  TraceId: string
  DurationMilliseconds: number
}

interface ApiResponse<T> {
  Success: boolean
  Message: string
  Data?: T
}

interface LogsPage {
  Items: HttpRequestLog[]
  TotalCount: number
  Page: number
  Size: number
}

interface TablePagination {
  sortBy: string
  descending: boolean
  page: number
  rowsPerPage: number
  rowsNumber: number
}

interface TableRequest {
  pagination: TablePagination
}

const columns = [
  { name: 'time', label: '时间', field: 'Time', align: 'left' as const },
  { name: 'method', label: '方式', field: 'Method', align: 'left' as const },
  { name: 'path', label: '请求路径', field: 'Path', align: 'left' as const },
  { name: 'result', label: '结果', field: 'StatusCode', align: 'center' as const },
  { name: 'duration', label: '耗时', field: 'DurationMilliseconds', align: 'right' as const, format: (value: number) => `${value} ms` },
  { name: 'actions', label: '详情', field: 'Id', align: 'center' as const }
]

const logs = ref<HttpRequestLog[]>([])
const loading = ref(false)
const errorsOnly = ref(false)
const showDetails = ref(false)
const selectedLog = ref<HttpRequestLog | null>(null)
const pagination = ref<TablePagination>({
  sortBy: 'Time',
  descending: true,
  page: 1,
  rowsPerPage: 50,
  rowsNumber: 0
})

const loadLogs = async (requestedPagination: TablePagination) => {
  loading.value = true
  try {
    const response = await api.get('/system/GetHttpRequestLogs', {
      params: {
        page: requestedPagination.page,
        size: requestedPagination.rowsPerPage,
        errorsOnly: errorsOnly.value
      }
    }) as ApiResponse<LogsPage>

    if (!response?.Success || !response.Data) {
      throw new Error(response?.Message || '获取 HTTP 请求日志失败')
    }

    logs.value = response.Data.Items
    pagination.value = {
      ...requestedPagination,
      page: response.Data.Page,
      rowsPerPage: response.Data.Size,
      rowsNumber: response.Data.TotalCount
    }
  } catch (error) {
    toast.error('获取 HTTP 请求日志失败', { autoClose: 2000, position: 'top-center' })
    console.error('Error getting HTTP request logs:', error)
  } finally {
    loading.value = false
  }
}

const onRequest = (request: TableRequest) => loadLogs(request.pagination)

const formatTime = (value: string) => new Date(value).toLocaleString()

const prettyJson = (value: string) => {
  try {
    return JSON.stringify(JSON.parse(value), null, 2)
  } catch {
    return value || '-'
  }
}

watch(errorsOnly, () => {
  pagination.value.page = 1
  loadLogs(pagination.value)
})

onMounted(() => {
  loadLogs(pagination.value)
})
</script>

<style scoped lang="scss">
.system-logs-page {
  padding: 20px;
}

.system-logs-page :deep(.q-table thead th),
.system-logs-page :deep(.q-table tbody td) {
  font-size: 14px;
}

.system-logs-page :deep(.q-table thead th) {
  font-weight: 600;
}

.detail-content {
  max-height: calc(100vh - 70px);
  overflow: auto;
  font-size: 15px;
}

.detail-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
  overflow-wrap: anywhere;
}

.full-row {
  grid-column: 1 / -1;
}

pre {
  max-height: 32vh;
  overflow: auto;
  padding: 12px;
  font-size: 14px;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  background: #f5f5f5;
  border-radius: 4px;
}

.exception-info {
  color: #b71c1c;
}

@media (max-width: 768px) {
  .system-logs-page {
    padding: 10px;
  }

  .detail-grid {
    grid-template-columns: 1fr;
  }

  .full-row {
    grid-column: auto;
  }
}
</style>
