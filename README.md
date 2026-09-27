# v2rayN-connstat

**带「Xray 连接」监控页的 v2rayN** —— 基于 [2dust/v2rayN](https://github.com/2dust/v2rayN) 7.24.9 补丁修改。

原版 v2rayN 在 Xray 内核模式下看不到每条连接的信息（「当前连接」页只支持 sing-box）。本补丁新增了独立的「**Xray 连接**」标签页：每条连接的**目标域名、实时上下行速度、累计流量、存活时长**，每秒刷新，一目了然。

> 内核侧请配合 → [xray-core-connstat](https://github.com/bianshuicheng/xray-core-connstat)（提供 connstat 连接监控数据的补丁版 Xray）

---

## ✨ 新功能：独立「Xray 连接」标签页

| 能力 | 说明 |
|---|---|
| **每秒实时刷新** | 进程（图标+名称）、主机（域名）、网络、入站、出站、↓/↑实时速度、↓/↑累计流量、存活时长，10 列表格 |
| **进程列** | 每条连接显示发起进程的**图标 + 名称**（内核侧附带 PID 与完整路径）；图标自动提取并缓存，不卡界面 |
| **DNS 流量过滤** | 三档筛选：全部连接 / 仅 DNS 流量 / 仅应用连接（非 DNS）；按目标端口 `:53` 与内核 DNS 通道 tag（`dns`、`dns-module`、`direct-dns-N`）自动识别 |
| **列宽记忆** | 表格列配置（列宽）持久化保存，重启后自动恢复 |
| **主机过滤** | 顶部过滤框，输入关键字即时筛选连接 |
| **自动刷新开关** | 顶部一键暂停/恢复刷新，方便看清楚某一条连接 |
| **列宽自适应** | 列宽自动适配内容，长域名不遮挡 |
| **TUN 模式可用** | 数据来自内核 connstat 补丁，TUN 模式开/关都能统计 |
| **修复「信息」页** | Xray 模式下「信息」标签页保持可见（原版在该模式下不可达） |

原有的「当前连接」标签页**保持官方 7.24.9 原样**，继续服务于 sing-box 内核，与 Xray 互不干扰。

## 📥 下载

到 [Releases](https://github.com/bianshuicheng/v2rayN-connstat/releases) 下载：

- `v2rayN.exe` —— Windows x64 自包含单文件版（.NET 10，含「Xray 连接」标签页）
- 多平台包（Windows / Linux / macOS × x64 / arm64）：在 Actions 页面运行 **`release connstat packages`** 工作流（填入 release_tag），自动构建并发布到对应 Release

## 🚀 快速开始

1. 下载本仓库 Release 的 `v2rayN.exe`
2. 下载 [xray-core-connstat](https://github.com/bianshuicheng/xray-core-connstat) 的 `xray-connstat.exe`，替换 v2rayN 目录下的 `bin\xray\xray.exe`
3. 重启 v2rayN → 主界面出现「**Xray 连接**」标签页，开启自动刷新即可看到每条连接的实时监控

## 🔧 与原版的区别

| 文件 | 改动 |
|---|---|
| `v2rayN/ServiceLib/ViewModels/XrayConnectionsViewModel.cs` | **新增**。「Xray 连接」页 ViewModel：每秒轮询 metrics `connstat`，计算实时速度/累计/存活时长，支持过滤与自动刷新；展示进程信息（process/pid/path） |
| `v2rayN/ServiceLib/Models/Dto/XrayConnectionModel.cs` | **新增**。连接行数据模型（含 process/pid/path 字段） |
| `v2rayN/ServiceLib/Models/Dto/XrayConnStat.cs` | **新增**。connstat JSON 的 DTO（含 process/pid/path 字段） |
| `v2rayN/v2rayN/Views/XrayConnectionsView.xaml(.cs)` | **新增**。「Xray 连接」视图（10 列表格：进程图标+名称 + 过滤 + 自动刷新 + 自适应列宽） |
| `v2rayN/v2rayN/Converters/ProcessIconConverter.cs` | **新增**。进程图标转换器：`Icon.ExtractAssociatedIcon` 提取进程图标 + `ConcurrentDictionary` 缓存，避免每秒刷新时重复提取卡顿 |
| `v2rayN/v2rayN/Common/SimpleViewLocator.cs` | **修复**。注册 `XrayConnectionsViewModel → XrayConnectionsView` 视图映射（缺失会导致「Xray 连接」页空白） |
| `v2rayN/v2rayN/Views/MainWindow.xaml` | 三个布局各新增 `tabXrayConnections*` 标签页 |
| `v2rayN/v2rayN/Views/MainWindow.xaml.cs` | 新标签页注入与可见性绑定（`ShowXrayConnectionsUI`）；「信息」页可见性改绑 `ShowXrayConnectionsUI` |
| `v2rayN/ServiceLib/ViewModels/MainWindowViewModel.cs` | 新增 `ShowXrayConnectionsUI` 与 `XrayConnectionsViewModel` |
| `v2rayN/ServiceLib/Resx/ResUI.resx / ResUI.zh-Hans.resx / ResUI.Designer.cs` | 新增 `TbXrayConnections`、`TbSortingInbound`、`TbSortingOutbound`、`TbSortingProcess` 资源键 |
| `v2rayN/GlobalHotKeys/` | **并入**。`.gitmodules` 引用的 2dust/GlobalHotKeys 子模块源码（官方源码 zip 不含子模块内容，不并入则 `v2rayN.Desktop` 无法编译） |
| `.github/workflows/release-connstat.yml` | **新增**。多平台自动构建发布工作流：以官方包为底合并 + 各平台交叉编译补丁版 connstat xray 内核 |
| `README.md` / `CONNSTAT.md` | **新增/替换**。本补丁的说明文档（原版说明在 `README-upstream.md`） |

## 🖥️ 平台差异说明

- 「Xray 连接」标签页属于 **Windows WPF 界面**（`v2rayN/v2rayN.csproj`）。
- Linux / macOS 包使用 `v2rayN.Desktop`（Avalonia）界面，**不含该标签页**；这两个平台仍可通过 xray 内核的 metrics 端口或 `connstat-view` 终端查看器使用连接监控。

## 🛠️ 从源码编译（Windows）

```bat
dotnet publish v2rayN\v2rayN.csproj -c Release -r win-x64 --self-contained true -o publish
```

## 🤖 多平台自动构建

- 推送到 `master` 自动触发上游 `build-windows` / `build-linux` / `build-macos`（Windows/Linux/macOS × x64/arm64 编译验证，产物在 Actions Artifacts）。
- Actions → **`release connstat packages`** → 填 `release_tag`（如 `v7.24.9-connstat`）→ Run：自动构建六个平台的 zip 包并发布到对应 Release（免签名，不依赖上游需要 GPG 密钥的发布流程）。

## 📝 更新记录

- **2026-09-27（v6.1）**：DNS 过滤默认值改为**「应用连接（非DNS）」**——默认隐藏内核 DNS 通道噪音，需要看 DNS 解析时手动切回「DNS 流量」或「全部连接」
- **2026-09-27（v6）**
  - **DNS 流量过滤**：新增三档筛选——**全部连接 / 仅 DNS 流量 / 仅应用连接（非 DNS）**。DNS 判定规则：目标端口 `:53`，或入站/出站 tag 属于内核 DNS 通道（`dns` 出站、`dns-module` DoH 入站、`direct-dns-N` 直连 DNS 入站）
  - **列宽记忆**：「Xray 连接」表格的列配置持久化保存，重启 v2rayN 后自动恢复上次调整的列宽
- **2026-09-27（v5）**
  - **修复 TUN 模式下「Xray 连接」页不刷新**：开启 TUN 时 v2rayN 以 sing-box 作为前置核，界面误判"运行内核不是 Xray"导致本页停止轮询；现在固定轮询 connstat 端点，TUN 开/关都正常显示
  - **新增 sing-box 主核回退**：主核为纯 sing-box（无 xray）时，自动改读 sing-box 的 clash API `/connections`（同样带进程名/路径元数据），本页在 sing-box 模式下也有完整的连接监控数据
  - 连接 Id 改为字符串（兼容 clash API 的 UUID）；新增连接类型（Type）显示；轮询逻辑重构为 Xray / sing-box 双数据源 + 死连接统一清理
- **2026-09-26（v4）**：新增「进程」列（图标 + 名称，内核附带 PID/路径；`ProcessIconConverter` 图标提取 + 缓存）
- **2026-09-26（v3）**：修复视图注册缺失导致的「Xray 连接」页空白；自动刷新改为默认开启
- **2026-09-26（初版）**：独立「Xray 连接」标签页，基于 xray-core-connstat 的 connstat 实时连接监控

## 🙏 致谢与许可

- 上游项目：[2dust/v2rayN](https://github.com/2dust/v2rayN)
- 内核补丁：[xray-core-connstat](https://github.com/bianshuicheng/xray-core-connstat)
- 许可证与上游一致：[GPL-3.0](LICENSE)，本补丁改动同样以 GPL-3.0 发布
