# MCP 智能体接入说明

博客内置了一个 MCP（Model Context Protocol）服务端，外部 AI 助手（Claude Desktop、Muse、Cherry Studio 等支持 MCP 的客户端）连上之后，可以直接读取、搜索、发布和维护本站文章。

## 端点地址

```
https://hangyejiadao.vip/api/mcp
```

- 传输方式：Streamable HTTP，单个 `POST` 端点，JSON-RPC 2.0，普通 JSON 响应（非 SSE 流）。
- **注意：不要用 `https://hangyejiadao.vip/mcp`**。博客自身的「端口转发」功能会把 `/mcp` 路径的请求转发到其他服务（站长挂在 `/mcp` 下的是另一台机器的 MCP 服务），博客自己的 MCP 服务特意挂在 `/api/mcp`，不会被转发规则命中。

## 开启与令牌（管理员操作）

1. 登录管理后台 → **系统设置** → **MCP 智能体发布**卡片。
2. 勾选「启用 MCP 接口」。
3. 点「生成新令牌」（48 位十六进制随机串），或自己填一个足够长的随机字符串。
4. 点右上角「保存配置」生效。
5. 同一卡片里的「测试连接」按钮可直接验证：成功会提示共 8 个可用工具；卡片下方还有现成的客户端配置 JSON，点「复制 JSON」即可。

令牌只存在系统设置（`McpPublishToken`）里，不在代码或 `appsettings.json` 中；泄露后在后台点「生成新令牌」并保存即可轮换。关闭「启用 MCP 接口」后接口一律返回 503。

## 客户端配置

### Claude Desktop（Streamable HTTP，推荐）

编辑 Claude Desktop 配置文件（Windows：`%APPDATA%\Claude\claude_desktop_config.json`；macOS：`~/Library/Application Support/Claude/claude_desktop_config.json`）：

```json
{
  "mcpServers": {
    "masuit-blog": {
      "url": "https://hangyejiadao.vip/api/mcp",
      "headers": {
        "Authorization": "Bearer <你的令牌>"
      }
    }
  }
}
```

### Muse / VS Code（`.muse/mcp.json` 或 `.vscode/mcp.json`）

```json
{
  "servers": {
    "masuit-blog": {
      "type": "http",
      "url": "https://hangyejiadao.vip/api/mcp",
      "headers": {
        "Authorization": "Bearer <你的令牌>"
      }
    }
  }
}
```

### 其他支持 Streamable HTTP 的客户端（Cherry Studio 等）

- 类型选 **Streamable HTTP**（不要选 SSE，也不要选 stdio）。
- URL 填 `https://hangyejiadao.vip/api/mcp`。
- 请求头加 `Authorization: Bearer <你的令牌>`。

## 鉴权说明

- **全部 8 个工具共用同一个 Bearer 令牌**，只读工具也不例外：因为同一个端点还提供发文、改文等写操作，且 `list_posts` / `search_posts` 可按状态查到草稿与已下架文章，所以不做“读公开、写鉴权”的拆分。令牌只发给可信的智能体。
- 未配置令牌时接口返回 `503`；令牌错误返回 `401`；令牌比较使用恒定时间比较。
- 如果以后想把只读工具公开（不带令牌也能查已发布文章），改造方向：在 `McpController.Handle` 里按工具名分流——`list_categories` / `list_posts` / `search_posts` / `get_post` / `list_tags` 未带令牌时强制限定 `status=published`；`publish_post` / `update_post` / `upload_image` 仍要求令牌。改之前要评估草稿泄露风险。

## 可用工具（共 8 个）

| 工具 | 说明 | 入参 | 返回 |
|---|---|---|---|
| `list_categories` | 列出全部可用文章分类 | 无 | `categories[]`：`id`、`name`、`parentId`、`description` |
| `list_posts` | 分页列出文章，可按标题关键词、分类、状态过滤 | `keyword?`（标题模糊匹配）、`categoryId?`、`status?`（`published` / `draft` / `takedown`）、`page`（默认 1）、`pageSize`（默认 20，最大 50） | `total`、`page`、`pageSize`、`posts[]`：`id`、`title`、`status`、`categoryId`、`categoryName`、`postDate`、`url` |
| `search_posts` | 按关键词全文搜索（标题、关键词、标签、正文） | `keyword`（必填，多个词空格分隔，按任一词匹配）、`status?`、`page`、`pageSize` | 同 `list_posts`，另有 `summary`（去 HTML 的纯文本摘要，约 200 字，命中词前后截取） |
| `get_post` | 按 id 读取单篇文章全文 | `id`（必填） | `id`、`title`、`content`、`contentType`（`markdown` / `html`）、`categoryId`、`categoryName`、`tags`、`keywords`、`status`、`author`、`postDate`、`modifyDate`、`url` |
| `list_tags` | 列出已有标签及文章数（按文章数降序，最多 200 个） | 无 | `tags[]`：`name`、`count` |
| `publish_post` | 发布一篇新文章 | `title`（必填，最长 128 字）、`content`（必填，最长 1000000 字）、`contentType?`（`markdown` 默认 / `html`）、`categoryId` 或 `categoryName`（二选一）、`tags?`、`keywords?`、`status?`（`published` 默认立即发布 / `draft` 存草稿） | `id`、`title`、`status`、`url` |
| `update_post` | 按 id 修改文章，只更新传入的字段 | `id`（必填）、`title?`、`content?`、`contentType?`、`categoryId?` / `categoryName?`、`tags?`（空字符串=清空）、`keywords?`、`status?`（`published` / `draft` / `takedown`）；不提供删除 | `id`、`title`、`status`、`url` |
| `upload_image` | 上传一张图片（base64），返回可直接写进正文的图片 URL | `fileName`（必填，含扩展名）、`dataBase64`（必填，可带 `data:image/...;base64,` 前缀） | `url`、`path`、`fileName`、`size`；仅支持 png/jpg/jpeg/gif/bmp/ico/webp，≤ 20MB，优先图床、失败回落本站 `/upload/images/` |

返回格式：每个工具的结果都包在 MCP 工具结果里（`content[0].type=text`，`text` 为上述 JSON；工具自身的校验错误以 `isError=true` 返回，不抛到协议层）。

发文约定（给智能体）：发文前先调 `list_categories` 确认分类、调 `list_tags` 复用已有标签；`publish_post` 的作者固定为站点第一个管理员账号；Markdown 正文仅去首尾空白，HTML 正文走与访客投稿一致的消毒与图片本地化管线。

## 用 curl 验证

把 `<你的令牌>` 换成后台生成的令牌：

```bash
# 1. initialize（握手，服务端回显客户端声明的协议版本）
curl -s -X POST https://hangyejiadao.vip/api/mcp \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <你的令牌>" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"curl-test","version":"1.0"}}}'

# 2. 列出工具（应返回 8 个）
curl -s -X POST https://hangyejiadao.vip/api/mcp \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <你的令牌>" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/list"}'

# 3. 全文搜索文章
curl -s -X POST https://hangyejiadao.vip/api/mcp \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <你的令牌>" \
  -d '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"search_posts","arguments":{"keyword":"MCP","pageSize":5}}}'

# 4. ping
curl -s -X POST https://hangyejiadao.vip/api/mcp \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <你的令牌>" \
  -d '{"jsonrpc":"2.0","id":4,"method":"ping"}'
```

预期：`initialize` 返回 `protocolVersion` + `capabilities.tools` + `serverInfo{name: masuit-myblogs-mcp, version: 1.0.0}`；`tools/list` 返回 8 个工具；`search_posts` 返回匹配文章的 `total` 与带 `summary` 的列表。

## 协议与实现说明

- 服务端实现：`src/Masuit.MyBlogs.Core/Controllers/McpController.cs`，手写 JSON-RPC 2.0（未引入官方 MCP C# SDK），单端点 `POST /api/mcp`，支持 `initialize` / `ping` / `tools/list` / `tools/call`，通知类消息（无 `id`，如 `notifications/initialized`）返回 `202` 无正文；不支持批量请求；单次请求体上限 42MB（需容纳 `upload_image` 的 base64 图片）。
- 为什么没用官方 SDK：该接口已随站点发布并配套了后台管理（开关、令牌生成/轮换、测试连接、客户端 JSON 自动生成），手写实现零新增依赖；官方 SDK（`ModelContextProtocol.AspNetCore`，`MapMcp()`）可作为后续重构选项，但重构要同步改后台管理页与 `/api/mcp` 路径，收益有限。
- 管理后台：`front/src/views/system/settings.vue` 的「MCP 智能体发布」卡片；令牌键 `McpPublishToken`、开关 `McpPublishEnabled` 存在系统设置表。
- 浏览器直接访问会跨域受限：生产环境未对 `/api/mcp` 开 CORS（仅 DEBUG 开 `AllowAny`），桌面客户端（Claude Desktop 等）不受影响；如需在网页里调，先在 `Startup` 里给 `/mcp` 或 `/api/mcp` 单独加窄范围的 CORS 策略。

## 常见问题

- **401 未授权**：令牌不对，或改了令牌还没点「保存配置」（测试连接校验的是已保存的令牌）。
- **503 接口未启用**：开关被关掉，或还没配置令牌。
- **连到 `/mcp` 没反应 / 被转发**：`/mcp` 是端口转发规则的挂载路径，请求会被代理到别的服务；博客 MCP 只能用 `/api/mcp`。
- **413 请求体过大**：`upload_image` 的图片 + base64 超过 42MB，或图片本身超过 20MB。
- **搜不到刚发布的文章**：`search_posts` / `list_posts` 直接查数据库，不依赖 Lucene 索引；搜不到请确认文章状态（草稿需传 `status=draft` 才能查到）与关键词。
