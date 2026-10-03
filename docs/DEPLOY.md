# 后端自动化部署指南

本项目通过 **GitHub Actions + Docker + SSH** 自动部署到服务器
`49.51.199.210`。每次推送到 `master` 分支涉及 `src/**` 的改动，都会自动构建镜像并发布。

> 前端管理后台由独立的 GitHub Actions 工作流构建，并只发布到服务器
> `/opt/myblogs/appwwwroot/dashboard`；宿主机的 `appwwwroot` 整目录挂载到容器，
> 上传文件及其他静态资源不会被前端发布覆盖。

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

服务器上目录结构（默认 `/home/<部署用户>/masuit-myblogs`，可在 GitHub 变量 `DEPLOY_PATH` 中改为
`/opt/masuit-myblogs` 等绝对路径；下文以 `$DEPLOY_PATH` 代称）：

```
$DEPLOY_PATH/
├── deploy/
│   ├── docker-compose.yml      # 由 git 同步
│   ├── .env.example            # 由 git 同步
│   ├── .env                    # 首次自动生成，需自行修改（不纳入 git）
│   ├── appsettings.json        # 首次自动生成，需自行修改（不纳入 git）
│   ├── App_Data/               # 首次自动生成（IP库/词库/证书，不纳入 git）
│   ├── data/                   # PostgreSQL + Redis 数据（自动创建）
│   ├── logs/  lucene/  appwwwroot/       # 运行时数据和静态文件
```

`docker-compose.yml` 会启动 3 个容器：`web`（本项目）、`postgres`、`redis`。
**如果你已自建数据库/Redis**，请编辑 compose 删除对应服务与 `depends_on`，并改 `appsettings.json` 的连接地址。

---

## 2. 服务器准备（一次性）

服务器为 Ubuntu，登录用户为 `ubuntu`（见本地 `~/.ssh/config`）。在服务器上执行：

```bash
# 1) 安装 Docker（官方脚本已包含 compose 插件）
curl -fsSL https://get.docker.com | sudo sh

# 2) 把部署用户加入 docker 组（否则流水线无法操作 Docker）
sudo usermod -aG docker ubuntu

# 3) 使组权限立即生效（重连 SSH，或执行：）
newgrp docker

# 4) 验证
docker run --rm hello-world
```

> 若不想用 docker 组，也可给该用户配置免密 sudo，流水线会自动回退到
> `sudo docker ...`（见 `deploy.yml` 中的 `Docker access` 探测）。
>
> **推荐**：用 Nginx/Caddy 反向代理到 `127.0.0.1:5000` 并处理 TLS，
> 此时请在 `appsettings.json` 中设置 `"Https": { "Enabled": false }`。

> 确保云厂商安全组放行 **22**（SSH）与 **5000**（应用）端口。

---

## 3. 配置 GitHub（一次性）

### 3.1 生成部署用 SSH 密钥

在**本地**执行（私钥只上传到 GitHub Secrets，绝不入库）：

```powershell
# 生成一对 ed25519 密钥
ssh-keygen -t ed25519 -C "github-actions-masuit-myblogs" -f "$env:USERPROFILE\.ssh\myblogs_deploy"
```

- **公钥** `~/.ssh/myblogs_deploy.pub` → 追加到服务器的 `~/.ssh/authorized_keys`
- **私钥** `~/.ssh/myblogs_deploy`（无扩展名）→ 填入 GitHub 的 `SERVER_SSH_KEY`

把公钥安装到服务器（在**本地** PowerShell 执行，最后一步会提示输入一次服务器密码）：

```powershell
# 1) 上传公钥
scp "$env:USERPROFILE\.ssh\myblogs_deploy.pub" ubuntu@49.51.199.210:/tmp/deploy.pub

# 2) 追加到 authorized_keys（会提示输入服务器密码）
ssh ubuntu@49.51.199.210 "mkdir -p ~/.ssh && chmod 700 ~/.ssh && cat /tmp/deploy.pub >> ~/.ssh/authorized_keys && chmod 600 ~/.ssh/authorized_keys && rm /tmp/deploy.pub && echo INSTALLED"

# 3) 验证免密登录（应输出 CONN_OK，且不再提示密码）
ssh -o BatchMode=yes ubuntu@49.51.199.210 "echo CONN_OK"
```

### 3.2 添加 Repository Secrets

打开 `Settings → Secrets and variables → Actions → New repository secret`，逐个添加：

| Secret | 必填 | 值 |
|---|---|---|
| `SERVER_HOST` | ✅ | `49.51.199.210` |
| `SERVER_USER` | ✅ | `ubuntu` |
| `SERVER_SSH_KEY` | ✅ | 本地私钥 `~/.ssh/myblogs_deploy` 的**全部内容**（含 `-----BEGIN/END OPENSSH PRIVATE KEY-----` 两行） |
| `SERVER_PORT` | ❌ | 不填则默认 `22` |

获取私钥全文并可一键复制到剪贴板：

```powershell
Get-Content "$env:USERPROFILE\.ssh\myblogs_deploy" -Raw | Set-Clipboard
# 然后在 GitHub 页面直接 Ctrl+V 粘贴
```

### 3.3 （可选）添加 Repository Variable

`Settings → Secrets and variables → Actions → Variables`（注意是 Variables，不是 Secrets）：

| Variable | 默认值 | 说明 |
|---|---|---|
| `DEPLOY_PATH` | `$HOME/masuit-myblogs` | 服务器上的部署目录，即 `/home/ubuntu/masuit-myblogs`。填 `/opt/masuit-myblogs` 亦可，但需保证该用户有写权限 |

---

## 4. 首次部署

1. 推送代码触发流水线（或到 `Actions → Deploy Backend → Run workflow` 手动触发）。
2. 流水线首次运行会自动把仓库 clone 到服务器并生成默认配置：
   - `deploy/appsettings.json`（复制自仓库默认值）
   - `deploy/App_Data/`
   - `deploy/.env`（复制自 `.env.example`）
3. **此时容器大概率启动失败**，因为默认配置还不能用。登录服务器修改配置：

```bash
cd ~/masuit-myblogs/deploy         # DEPLOY_PATH 为默认值时

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
cd ~/masuit-myblogs/deploy

# 1) 解压备份（仓库里的 database/postgres/myblogs.7z）
#    可用 7z 解压得到 myblogs.sql
7z x ~/masuit-myblogs/database/postgres/myblogs.7z -o/tmp

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
| 查看状态 | `cd ~/masuit-myblogs/deploy && docker compose ps` |
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
| `deploy/appwwwroot` | `/app/wwwroot` | 静态资源、前端后台和用户上传文件 |
| `deploy/App_Data` | `/app/App_Data` | IP 库、词库、证书 |
| `deploy/appsettings.json` | `/app/appsettings.json` | 配置（只读挂载） |

建议定期备份 `deploy/data/`、`deploy/appwwwroot/`、`deploy/appsettings.json`。

---

## 8. 本地构建镜像（可选）

无需服务器即可验证镜像能否构建：

```bash
python deploy/build_backend.py --tag masuit-myblogs:local
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

**Q: 前端改动会触发部署吗？**
会。推送 `front/**` 改动到 `master` 后，`Deploy Dashboard` 工作流会构建前端，
并只同步 `dashboard` 子目录到 `/opt/myblogs/appwwwroot/dashboard`。
也可在 GitHub Actions 页面手动运行 `Deploy Dashboard`。发布过程不会覆盖
`upload` 或 `appwwwroot` 中的其他文件。

**Q: 镜像更新后前端仍显示旧版怎么办？**
应用镜像不负责发布前端。请检查 `Deploy Dashboard` 工作流是否成功；该工作流
直接更新宿主机挂载目录中的 `dashboard`，无需重启应用容器。

## 后端发布版本信息

本地与 GitHub Actions 共用 `deploy/build_backend.py`，每次构建生成独立版本号：
`yyyyMMdd.HHmmss-<8位提交号>-<8位随机后缀>`，时间使用北京时间（UTC+8）。
同一个 Git 提交重复构建，也会产生不同版本号。

本地构建（需要 Python 3、Git、Docker）：

```powershell
python deploy/build_backend.py
# 或指定镜像标签，并保存一份元数据（输出文件请放到仓库外，避免被计为未提交改动）
python deploy/build_backend.py --tag myblogs/app:local --output "$env:TEMP/myblogs-version.json"
```

镜像内 `/app/version.json` 记录：版本号、UTC/北京时间、完整提交号、分支、工作区
是否有未提交改动、最近 20 条提交的作者/提交时间/完整提交日志，以及 CI 运行编号。
本地有未提交改动时 `git.dirty=true`；提交日志只对应已提交的 Git 历史，并非未提交
代码的内容证明。打包时间指构建脚本开始执行时间，而不是上传、部署或容器启动时间。

文件位于应用根目录，不受 `wwwroot`、`App_Data` 和配置文件挂载覆盖，不提供公开 HTTP
接口。镜像的 OCI 标签同时记录版本号、提交号和打包时间。运行时读取：

```bash
sudo docker exec myblogs-app cat /app/version.json
sudo docker inspect myblogs-app --format '{{.Config.Image}}'
sudo docker image inspect myblogs/app:local --format '{{json .Config.Labels}}'
```

GitHub Actions 自动读取完整 Git 历史、生成元数据并传入 Docker，同时推送版本号标签。
不带元数据的裸 `docker build` 会明确失败，请使用上面的脚本，避免发布缺少追溯信息的镜像。