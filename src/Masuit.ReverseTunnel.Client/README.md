# Masuit Reverse Tunnel Web Client

这是内网穿透客户端的 Web/Docker 版本。容器启动后打开 `http://客户端地址:18080/`，填写服务端地址、隧道编号、服务端密钥和内网目标服务即可。

## SQLite 持久化

客户端配置保存在 `/data/client.db`。部署时必须挂载持久化目录，例如：

```yaml
services:
  reverse-tunnel-client:
    build: .
    ports:
      - "18080:8080"
    volumes:
      - ./data:/data
    extra_hosts:
      - "host.docker.internal:host-gateway"
    restart: unless-stopped
```

如果目标服务在宿主机上，目标主机填写 `host.docker.internal`；如果目标服务在同一个 Docker Compose 网络中，填写对应服务名。

## 安全边界

- 服务端规则的密钥必须与客户端一致，服务端数据库只保存密钥的 SHA-256 摘要。
- 客户端 SQLite 文件包含明文密钥，应限制宿主机目录权限，不要提交到 Git。
- Web 管理端口只应暴露给可信管理网络，生产环境建议通过 HTTPS、反向代理认证或防火墙限制访问。配置查询接口不会回传已保存的明文密钥。
- 服务端 `ReverseTunnel:AllowedClientIps` 可以继续限制客户端容器的出口 IP 或 CIDR。
