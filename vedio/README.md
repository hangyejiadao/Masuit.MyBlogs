# Streambert 网页移植版

移植自 `truelockmc/streambert` 2.6.0，来源提交见 `UPSTREAM.json`。保留上游 React 界面、首页轮播、搜索和资料库，并将 Electron 播放和设置改为浏览器实现。此目录及修改遵循 GPL-3.0，保留原许可证。

访问地址：`https://hangyejiadao.vip/vedio`，目录请求自动跳转到 `/vedio/`。

## 本地运行

需要 Node.js 22.12 或更新版本。

```sh
cd vedio
npm ci
npm run dev
```

打开 `http://127.0.0.1:5173/vedio/`。首次使用填写自己的 TMDB **API Read Access Token**（长 JWT），不是短 API Key，参见 `tmdb-tutorial.md`。Token 和收藏、观看历史保存在当前浏览器，不写入服务器配置或发布文件。浏览器本地存储不提供操作系统密钥链加密。

## 网页功能

- 首页影视推荐、搜索、电影和电视剧详情、季集选择、预告片。
- 收藏排序、观看历史、手动已看标记、备份导出和导入。
- Videasy、VidSrc 和 Vidking iframe 播放；可切换源、全屏或新窗口打开。
- 影视资料语言、主题、强调色、默认播放源设置。
- 支持桌面和手机布局，字体随发布包提供。

网页版无法使用 Electron 的 AllManga 抓取、多线程本地下载器、广告拦截、跨站视频脚本、Discord 状态、桌面自动更新和窗口控制。此版本移除了这些操作。播放进度仅接受实际 iframe、匹配播放源域名及影片/季集的 `PLAYER_EVENT` 或 `MEDIA_DATA` 消息；未提供进度的播放源可手动标记已看。第三方播放源是否可用、是否允许 iframe 嵌入由其提供方决定。

## 构建和验证

```sh
npm test
npx playwright install chromium
npm run test:browser
npm run build
```

`dist/` 包含 `/vedio/` 静态资源、许可证、对应版本的源码压缩包和 `version.json`（源代码哈希和文件 SHA-256）。浏览器测试使用模拟 TMDB 响应，不包含真实 Token，也不依赖第三方视频站。真实影视目录和视频播放需配置有效 Token 后验证。

## 部署

服务器现有 ASP.NET Core 配置使用 `UseDefaultFiles().UseStaticFiles()`，且 `/home/work/myblogs/appwwwroot` 挂载至容器 `/app/wwwroot`。将发布包写入该目录的 `vedio/` 即可，无需重启博客或修改 Nginx。

```sh
tar -czf streambert-web.tar.gz -C vedio/dist .
# 上传至服务器 /tmp/streambert-web.tar.gz，同时上传 vedio/scripts/publish.sh
ssh ubuntu@SERVER 'bash /tmp/publish.sh /tmp/streambert-web.tar.gz /home/work/myblogs'
```

`vedio/scripts/publish.sh` 在写入前验证发布包，备份已有 `vedio/`，先同步带哈希的资源，再原子替换首页。历史哈希资源保留以支持更新前已打开的页面。`.github/workflows/deploy-vedio.yml` 复用已有 `SERVER_HOST`、`SERVER_USER`、`SERVER_SSH_KEY`、可选 `SERVER_PORT` 和 `DEPLOY_PATH` 变量；推送至 `master` 后只发布此目录。
