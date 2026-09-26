# v2rayN connstat 补丁版（独立「Xray 连接」标签页）

> 基于 v2rayN 官方 **7.24.9** 源码：新增独立的「**Xray 连接**」标签页，配合 [xray-core connstat 补丁内核](https://github.com/bianshuicheng/xray-core-connstat) 实现每条连接的域名 + 实时上下行速度监控。
> 原有「当前连接」页保持官方 7.24.9 原样，仅供 sing-box 内核使用，与 Xray 互不干扰。

## 使用

1. 重启 v2rayN（托盘退出再打开，或主界面「重启服务」），内核使用 connstat 补丁版 xray。
2. 主界面出现独立的「**Xray 连接**」标签页：
   - 列：**进程（图标+名称）｜主机（域名）｜网络｜入站｜出站｜↓实时速度｜↑实时速度｜↓累计｜↑累计｜存活时长**
   - 每秒自动刷新；顶部有主机过滤框和「自动刷新」开关；列宽自适应
   - **TUN 模式开/关都有效**
3. 「信息」标签页在 Xray 模式下保持可见。
4. 终端版查看器（xray-core-connstat 仓库的 `connstat-view.exe`）照旧可用。

## 源码改动（相对官方 7.24.9）

| 文件 | 改动 |
|---|---|
| `v2rayN/ServiceLib/ViewModels/XrayConnectionsViewModel.cs` | **新增**。Xray 专用连接页 VM：每秒轮询 metrics `connstat`，计算实时速度/累计/存活时长，支持过滤与自动刷新开关；展示进程信息（process/pid/path），`AutoRefresh` 构造时默认开启（不读共享配置） |
| `v2rayN/ServiceLib/Models/Dto/XrayConnectionModel.cs` | **新增**。连接行数据模型（含 process/pid/path 字段） |
| `v2rayN/ServiceLib/Models/Dto/XrayConnStat.cs` | **新增**。connstat JSON 的 DTO（含 process/pid/path 字段） |
| `v2rayN/v2rayN/Views/XrayConnectionsView.xaml(.cs)` | **新增**。「Xray 连接」视图（10 列表格：进程图标+名称 + 过滤 + 自动刷新 + 自适应列宽） |
| `v2rayN/v2rayN/Converters/ProcessIconConverter.cs` | **新增**。进程图标转换器：`Icon.ExtractAssociatedIcon` 提取 + `ConcurrentDictionary` 缓存，避免每秒刷新重复提取 |
| `v2rayN/v2rayN/Common/SimpleViewLocator.cs` | **修复**。注册 `XrayConnectionsViewModel → XrayConnectionsView` 映射（缺失会导致「Xray 连接」页空白） |
| `v2rayN/v2rayN/Views/MainWindow.xaml` | 三个布局各新增 `tabXrayConnections*` 标签页 |
| `v2rayN/v2rayN/Views/MainWindow.xaml.cs` | 新标签页的内容注入与可见性绑定（`ShowXrayConnectionsUI`）；「信息」标签页可见性改绑 `ShowXrayConnectionsUI`（修复 Xray 模式下信息页不可达） |
| `v2rayN/ServiceLib/ViewModels/MainWindowViewModel.cs` | 新增 `ShowXrayConnectionsUI` 与 `XrayConnectionsViewModel` |
| `v2rayN/ServiceLib/Resx/ResUI.resx / ResUI.zh-Hans.resx / ResUI.Designer.cs` | 新增 `TbXrayConnections`、`TbSortingInbound`、`TbSortingOutbound`、`TbSortingProcess` 资源键 |
| `v2rayN/GlobalHotKeys/` | **并入**。`.gitmodules` 引用的 2dust/GlobalHotKeys 子模块源码（官方源码 zip 不含子模块内容，不并入则 `v2rayN.Desktop` 无法编译） |
| `.github/workflows/release-connstat.yml` | **新增**。免签名多平台自动构建发布工作流（见下） |

## Windows 编译

```bat
dotnet publish v2rayN\v2rayN.csproj -c Release -r win-x64 --self-contained true -o publish
```

## 多平台自动构建（GitHub Actions）

- 推送到 `master` 会自动触发上游的 `build-windows` / `build-linux` / `build-macos` 三个工作流（六个目标，产物在 Actions Artifacts）。
- **发布完整多平台包**：Actions → `release connstat packages` → 填 `release_tag`（如 `v7.24.9-connstat`）→ Run。该工作流自动构建 windows / linux / macos × x64 / arm64 六个 zip 包并挂到对应 Release（无需配置 GPG 密钥，不依赖上游需要 `GPG_PRIVATE_KEY` 的签名发布流程）。

## 平台差异说明

- 「Xray 连接」标签页属于 **Windows WPF 界面**（`v2rayN/v2rayN.csproj`）；Linux/macOS 包使用 `v2rayN.Desktop`（Avalonia）界面，**不含该标签页**。Linux/macOS 上仍可通过 xray 内核的 metrics 端口或 `connstat-view` 终端查看器使用连接监控。

## 许可

与上游一致：GPL-3.0。补丁改动同样以 GPL-3.0 发布。
