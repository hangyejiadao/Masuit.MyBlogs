# 内网穿透客户端

## 目标

在 Masuit Blog 的端口转发功能中增加反向隧道模式：公网服务端监听对外端口，内网客户端主动连接服务端，服务端把公网 TCP 连接转交给客户端所在网络中的目标服务。

## 适用范围

- 服务端：`Masuit.MyBlogs.Core`，运行在 Docker 或普通 ASP.NET Core 主机。
- 客户端：`src/Masuit.ReverseTunnel.Client`，ASP.NET Core Web 客户端，使用 Docker 部署。
- 传输：客户端与服务端之间使用 WebSocket；目标业务流量使用二进制 WebSocket 帧转发。

## 服务端配置

在 `appsettings.json` 或环境变量中配置客户端来源白名单：

```json
"ReverseTunnel": {
  "AllowedClientIps": ["10.0.0.0/8", "192.168.1.20"]
}
```

白名单为空表示不限制来源 IP，但不会关闭密钥校验。支持 IPv4、IPv6、单个 IP 和 CIDR。服务端部署在反向代理后时，必须正确配置可信的转发头，否则白名单可能匹配到代理地址。

端口转发规则的监听端口仍必须位于 `10000-10100`，并且需要把对应端口发布到宿主机。例如 Docker 需要发布 `10000:10000`；只在容器内监听而未发布的端口无法从公网访问。

## 密钥校验

创建“内网穿透”规则时填写：

- `TunnelId`：只能包含字母、数字、`-` 和 `_`。
- `TunnelToken`：长度 16-256 个字符。

服务端数据库只保存密钥的 SHA-256 摘要，不保存明文。客户端控制通道 `/tunnel/control` 和每条数据通道 `/tunnel/data` 都必须提交相同密钥；任一通道校验失败都会被关闭。修改规则密钥会让旧客户端立即失效。

## Docker 客户端

客户端数据库位于 `/data/client.db`，必须挂载持久化目录：

```yaml
services:
  reverse-tunnel-client:
    build: ./src/Masuit.ReverseTunnel.Client
    ports:
      - "127.0.0.1:8080:8080"
    volumes:
      - ./reverse-tunnel-data:/data
    extra_hosts:
      - "host.docker.internal:host-gateway"
    restart: unless-stopped
```

打开 `http://127.0.0.1:8080/` 配置客户端。目标服务在宿主机时填写 `host.docker.internal`；目标服务在同一 Compose 网络时填写服务名。客户端管理页面没有内置用户系统，生产环境必须限制管理端口来源，或在反向代理层增加 HTTPS 与认证。配置查询接口不会回传 SQLite 中的明文密钥，保存配置时密钥留空表示沿用原密钥。

## 验收门禁

1. 服务端创建一条启用的反向隧道规则，并发布对应监听端口。
2. 客户端填写正确的服务端地址、隧道编号、密钥和本地目标端口，状态变为 `Connected`。
3. 使用错误密钥连接时，服务端不返回 `welcome`，客户端不能建立隧道。
4. 使用不在 `AllowedClientIps` 中的来源地址连接时，服务端返回 `403` 并拒绝 WebSocket。
5. 通过公网监听端口访问目标服务，确认双向 TCP 数据可用。
6. 重启客户端容器，确认 SQLite 中的配置仍存在；修改服务端密钥后，旧客户端不能重新连接。

## 风险与限制

- `/data/client.db` 包含客户端明文密钥，应限制宿主机目录权限，不得提交到 Git 或公开备份。
- Web 客户端所在容器必须能够访问内网目标地址；Docker 网络隔离或防火墙可能导致目标不可达。
- 当前实现只提供 TCP 端口级转发，不提供 UDP 转发、HTTP 路径级反向隧道或多客户端共享同一隧道编号。
- 本地环境无法模拟真实公网、Docker NAT 和代理转发，部署前应按上述验收步骤进行联调。
