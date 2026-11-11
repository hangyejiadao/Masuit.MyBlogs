import api from "@/axios/AxiosConfig"
import type { ApiResponse } from "./PostApi"

/**
 * 微信公众号推送通道状态（后端 WeChatMp/GetStatus 返回的 Data）
 */
export interface WeChatMpStatus {
  enabled: boolean
  configured: boolean
  autoPublish: boolean
  hint: string
}

/**
 * 文章推送结果（后端 WeChatMp/Push 返回的 Data，即 WeChatMpPushResult）
 */
export interface WeChatMpPushResultData {
  Success: boolean
  Message: string
  Status: number
  RecordId: number
  MediaId?: string
  PublishId?: string
  ArticleUrl?: string
  PublishAttempted: boolean
  PublishError?: string
  Warnings: string[]
}

/**
 * 微信公众号文章推送 API：博客文章同步到公众号草稿箱（可选自动发表）。
 * 按「个人订阅号」设计：默认只创建草稿，发表/群发在公众号后台或「公众号助手」App 手动完成。
 */
export class WeChatMpApi {
  private static statusPromise: Promise<ApiResponse<WeChatMpStatus>> | null = null

  /**
   * 推送通道状态：是否启用、是否已配好凭据、是否开启自动发表
   */
  static async getStatus(): Promise<ApiResponse<WeChatMpStatus>> {
    const response = await api.get('/WeChatMp/GetStatus')
    return response
  }

  /**
   * 推送通道状态（同一页面内只请求一次，多处共用）
   */
  static getStatusCached(): Promise<ApiResponse<WeChatMpStatus>> {
    if (!this.statusPromise) {
      this.statusPromise = this.getStatus()
    }
    return this.statusPromise
  }

  /**
   * 把一篇文章推送到公众号：建草稿，可选自动发表。
   * 正文图片多时耗时较长，超时放宽到 3 分钟。
   */
  static async push(postId: number, publish = false): Promise<ApiResponse<WeChatMpPushResultData>> {
    const response = await api.post(`/WeChatMp/Push?id=${postId}&publish=${publish}`, null, { timeout: 180000 })
    return response
  }

  /**
   * 取一篇文章最近一次的推送记录
   */
  static async getRecord(postId: number): Promise<ApiResponse<any>> {
    const response = await api.get(`/WeChatMp/GetRecord?id=${postId}`)
    return response
  }

  /**
   * 对一条「发表中」的推送记录查一次发表状态
   */
  static async refreshStatus(recordId: number): Promise<ApiResponse<any>> {
    const response = await api.post(`/WeChatMp/RefreshStatus?id=${recordId}`, null)
    return response
  }
}
