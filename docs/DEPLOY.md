# 后端自动化部署指南

本项目的后端（`src/Masuit.MyBlogs.Core`）通过 **GitHub Actions + Docker + SSH** 自动部署到服务器
`49.51.199.210`。每次推送到 `master` 分支涉及 `src/**` 的改动，都会自动构建镜像并发布。

> 本指南只覆盖**后端**。前端管理后台（`front/`）的构建产物
> `src/Masuit.MyBlogs.Core/wwwroot/dashboard` 已提交进仓库，无需在流水线中构建。

---

## 1. 部署架构

```mermaid
flowchart LR
    A[推送 master] --> B[GitHub Actions]
    B --> C[构建 Docker 镜像]
    C --> D[推送 GHCR]
    D --> E[SSH 登录服务器]
    E --> F[docker compose pull]
    F --> G[重启 web 容器]
    G --> H[健康检查]
```

服务器上目录结构（默认 `/opt/masuit-myblogs`）：

```
/opt/masuit-myblogs/
├── deploy/
│   ├── docker-compose.yml      # 由 git 同步
│   ├── .env.example            # 由 git 同步
│   ├── .env                    # 首次自动生成，需自行修改（不纳入 git）
│   ├── appsettings.json        # 首次自动生成，需自行修改（不纳入 git）
│   ├── App_Data/               # 首次自动生成（IP库/词库/证书，不纳入 git）
│   ├── data/                   # PostgreSQL + Redis 数据（自动创建）
│   ├── logs/  lucene/  wwwroot-upload/   # 运行时数据（自动创建）
```

`docker-compose.yml` 会启动 3 个容器：`web`（本项目）、`postgres`、`redis`。
**如果你已自建数据库/Redis**，请编辑 compose 删除对应服务与 `depends_on`，并改 `appsettings.json` 的连接地址。

---

## 2. 服务器准备（一次性）

以 Ubuntu 22.04/24.04 为例，使用 `root` 或具有 docker 权限的用户：

```bash
# 1) 安装 Docker（含 compose 插件）
curl -fsSL https://get.docker.com | sh

# 2) 创建部署目录
mkdir -p /opt/masuit-myblogs

# 3) 开放端口（如使用云服务器安全组，也需在控制台放行）
#    应用默认监听 5000
```

> **推荐**：用 Nginx/Caddy 反向代理到 `127.0.0.1:5000` 并处理 TLS，
> 此时请在 `appsettings.json` 中设置 `"Https": { "Enabled": false }`。

---

## 3. 配置 GitHub（一次性）

### 3.1 生成部署用 SSH 密钥

在**本地**执行（不要上传私钥到仓库）：

```bash
# 生成密钥（不要设置空口令以外的交互；执行后 ./deploy_key 是私钥）
ssh-keygen -t ed25519 -C "github-actions-deploy" -f ./deploy_key
```

- 把 `deploy_key.pub` 内容追加到服务器的 `~/.ssh/authorized_keys`
- `deploy_key`（私钥）内容用于下面的 `SERVER_SSH_KEY` 密钥

### 3.2 添加 Repository Secrets

`Settings → Secrets and variables → Actions → New repository secret`：

| Secret | 必填 | 说明 |
|---|---|---|
| `SERVER_HOST` | ✅ | `49.51.199.210` |
| `SERVER_USER` | ✅ | 登录用户名，如 `root` |
| `SERVER_SSH_KEY` | ✅ | 上一步生成的**私钥**全文 |
| `SERVER_PORT` | ❌ | SSH 端口，默认 `22` |

### 3.3 （可选）添加 Repository Variable

`Settings → Secrets and variables → Actions → Variables`：

| Variable | 默认值 | 说明 |
|---|---|---|
| `DEPLOY_PATH` | `/opt/masuit-myblogs` | 服务器上的部署目录 |

---

## 4. 首次部署

1. 推送代码触发流水线（或到 `Actions → Deploy Backend → Run workflow` 手动触发）。
2. 流水线首次运行会自动把仓库 clone 到服务器并生成默认配置：
   - `deploy/appsettings.json`（复制自仓库默认值）
   - `deploy/App_Data/`
   - `deploy/.env`（复制自 `.env.example`）
3. **此时容器大概率启动失败**，因为默认配置还不能用。登录服务器修改配置：

```bash
cd /opt/masuit-myblogs/deploy

# 修改 .env：设置数据库密码
vi .env                      # 修改 POSTGRES_PASSWORD

# 修改 appsettings.json：
#   - Https:Enabled        => false（若由 Nginx 终止 TLS）
#   - Database:ConnString  => Host=postgres;Username=postgres;Password=<与上面一致>;Database=myblogs
#   - Redis                => redis:6379,allowadmin=true
#   - 其它按需配置（CDN 真实 IP、邮件、图床等）
vi appsettings.json

# 手动重启使其生效
docker compose up -d
docker compose logs -f web
```

4. 在 `Actions` 页面重新运行一次流水线，或再次 `push`，健康检查通过即部署成功。

---

## 5. 导入数据库

首次部署是空库，需要还原数据。仓库中提供了备份：

```bash
cd /opt/masuit-myblogs/deploy

# 1) 解压备份（仓库里的 database/postgres/myblogs.7z）
#    可用 7z 解压得到 myblogs.sql
7z x /opt/masuit-myblogs/database/postgres/myblogs.7z -o/tmp

# 2) 还原到容器内的 postgres
docker compose exec -T postgres psql -U postgres -d myblogs < /tmp/myblogs.sql

# 3) 还原后重置 id 序列（见 database/postgres/readme.txt）
docker compose exec -T postgres psql -U postgres -d myblogs -c "$(cat <<'SQL'
SELECT concat('SELECT setval(''"',c.relname,'"'', MAX("',SPLIT_PART(c.relname, '_', 2),'")) FROM "',SPLIT_PART(c.relname, '_', 1),'";')
FROM pg_class c WHERE c.relkind = 'S';
SQL
)"
```

> 上面的第 3 步会生成一批 `SELECT setval(...)` 语句，把它们**执行一遍**即可。

---

## 6. 日常使用

| 操作 | 命令 |
|---|---|
| 查看状态 | `cd /opt/masuit-myblogs/deploy && docker compose ps` |
| 查看日志 | `docker compose logs -f --tail=100 web` |
| 重启 | `docker compose restart web` |
| 更新到最新镜像 | `docker compose pull web && docker compose up -d` |
| 进入 web 容器 | `docker compose exec web bash` |

默认后台地址：`http://<服务器IP>:5000/dashboard`
（初始用户名 `masuit`，密码 `123abc@#$`，**请立即修改**）

---

## 7. 存储与数据安全

以下目录通过 volume 挂载，容器重建不会丢失：

| 宿主机目录 | 容器内路径 | 内容 |
|---|---|---|
| `deploy/data/postgres` | `/var/lib/postgresql/data` | 数据库 |
| `deploy/data/redis` | `/data` | Redis AOF |
| `deploy/logs` | `/app/logs` | 应用日志 |
| `deploy/lucene` | `/app/lucene` | Lucene 搜索索引 |
| `deploy/wwwroot-upload` | `/app/wwwroot/upload` | 用户上传文件 |
| `deploy/App_Data` | `/app/App_Data` | IP 库、词库、证书 |
| `deploy/appsettings.json` | `/app/appsettings.json` | 配置（只读挂载） |

建议定期备份 `deploy/data/`、`deploy/wwwroot-upload/`、`deploy/appsettings.json`。

---

## 8. 本地构建镜像（可选）

无需服务器即可验证镜像能否构建：

```bash
docker build -t masuit-myblogs:local .
docker run --rm -p 5000:5000 \
  -v "$PWD/src/Masuit.MyBlogs.Core/appsettings.json:/app/appsettings.json:ro" \
  masuit-myblogs:local
```

---

## 9. 常见问题

**Q: 容器反复重启 / 健康检查失败？**
先看日志：`docker compose logs --tail=100 web`。最常见原因：
- `appsettings.json` 里连的还是 `127.1` / `127.0.0.1`，容器内应改用服务名 `postgres` / `redis`
- `Https:Enabled` 为 `true` 但 `App_Data/cert/server.pfx` 无效或密码不对 → 改为 `false`

**Q: 流水线报权限错误（permission denied）？**
GHCR 包默认私有。若服务器拉取镜像失败，二选一：
- 在 GitHub 上把该 package 设为 **Public**；或
- 在服务器执行 `docker login ghcr.io`（用有 `read:packages` 权限的 PAT）

**Q: 如何手动触发一次部署？**
`Actions → Deploy Backend → Run workflow`。

**Q: 只想部署后端，前端改动会触发吗？**
不会。流水线仅监听 `src/**`、`Dockerfile`、`deploy/**` 等路径的变更。
