# 相较官方 v2rayN 的修改说明

本仓库基于官方 v2rayN 7.24.9（Windows WPF 界面），在官方代码之上叠加了以下修改。全部修改在 git 历史中可查，本文按功能域归纳；涉及的主要源码文件随条目标注。

---

## 一、「Xray 连接」标签页（核心新增）

配合 [xray-core-connstat](https://github.com/bianshuicheng/xray-core-connstat) 补丁内核的 connstat 数据，提供官方没有的按连接/按程序监控面板：

- 每条连接展示：**进程（图标 + 名称）**、目标主机/域名、实时上下行速率、累计流量、存活时长、协议识别（嗅探）、入站/出站 tag、来源地址
- 每秒轮询内核 metrics 端口（`/debug/vars`），TUN 模式与 SOCKS 模式均可用
- 分组视图：**程序**（程序与其域名/IP 合并为常开卡片）/ 域名·IP / 出站 / 入站 / 按连接（单条明细）
- **活跃 / 已关闭** 选项卡：已关闭页由 GUI 本地归档（上限 300 条、保留 30 分钟），展示最终流量与关闭时刻
- DNS 流量过滤（全部 / 仅 DNS / 仅应用连接）、列宽持久化、按程序聚合的 PID 汇总

主要文件：`ServiceLib/ViewModels/XrayConnectionsViewModel.cs`、`ServiceLib/Models/Dto/XrayFlowVars.cs`、`ServiceLib/Models/Dto/XrayConnectionRows.cs`、`ServiceLib/Services/XrayFlowApiManager.cs`、`v2rayN/Views/XrayConnectionsView.xaml(.cs)`。

## 二、稳定性修复

| 位置 | 官方行为 | 修改后 |
|---|---|---|
| `ServiceLib/ViewModels/MsgViewModel.cs` | 消息页失活后，`AppendQueueMsg` 每次追加都抛「no registration」异常并被回投队列，形成**每秒 1-2 次的自持续异常循环**（GUI 内存增长与日志刷屏来源） | ViewModel 构造时注册常驻默认处理器：视图激活时 UI 优先，失活后消息留在有界队列，重开页面自动刷出 |
| `XrayConnectionsViewModel.cs` | TextFilter 为 null 时每次刷新抛 ArgumentNullException | 修复 |

## 三、界面调整

- **内核内存读数**：显示在主窗口左侧导航「Xray 连接」项下方（工作集总占用，悬停看 Go 堆/已保留细节），核心未运行时自动隐藏
- 状态栏汇总行兼顾提示显示

## 四、与官方的行为差异提示

1. 「Xray 连接」页依赖 xray-core-connstat 补丁内核；官方内核下该页自动隐藏（sing-box 主核时「当前连接」页仍为官方原样）
2. 消息页关闭再打开期间，消息不再逐条上屏（保留在 500 条有界队列中，重开页面一次性刷出）

---

配套内核：[xray-core-connstat](https://github.com/bianshuicheng/xray-core-connstat)（connstat 连接监控 + TUN 断网风暴治理，详见其 [MODIFICATIONS.md](https://github.com/bianshuicheng/xray-core-connstat/blob/main/MODIFICATIONS.md)）。
