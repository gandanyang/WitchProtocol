# 《魔女协议》Witch Protocol

> **记忆被剥夺时，还剩什么？**
>
> 二次元 · 弹幕 RPG —— Godot 4.7 + C#

《魔女协议》是一款二次元弹幕 RPG。玩家扮演失去记忆的魔女，在弹幕战斗中夺回属于自己的记忆与身份。核心循环：**弹幕闪避 → 释放弹幕 → 收集记忆碎片 → 拼回自我**。

> 当前为**地基阶段**：工程骨架、分层架构、核心战斗系统、测试机制与开发纪律文档已就位。尚未进入可玩内容开发。

## 快速开始

前置：Godot 4.7.1 mono（本机路径 `G:\Godot_v4.7.1-stable_mono_win64`）+ .NET 8 SDK。

```powershell
# 1) 打开编辑器（首次会自动导入 + 编译 C#）
.\tools\open_editor.ps1

# 2) 只构建 .NET 程序集
.\tools\build.ps1

# 3) headless 跑地基探针（自动化验证，5 个探针全过 = 地基健康）
.\tools\run_tests.ps1

# 4) 指定探针
.\tools\run_tests.ps1 boot,pool
```

> 开发纪律见 `AGENTS.md`；测试契约见 `docs/dev/TestSystem.md`；存档红线见 `docs/dev/SaveSystem.md`。

## 技术栈

| 层 | 技术 |
|---|---|
| 游戏引擎 | Godot 4.7.1 mono |
| 语言 | C# / .NET 8 |
| 渲染 | Forward Plus（桌面） |
| 自动化验证 | DevTestHub 探针（headless） |

## 目录结构

```
magicThunder/
├── project.godot          # 工程配置（autoload 单例 / 输入映射 / 自定义 user 目录）
├── magicThunder.csproj    # C# 项目（.NET 8）
├── scenes/                # 场景文件（Boot 启动场景等）
├── data/                  # 数据驱动 .tres 资源（PlayerConfig 等）
├── scripts/
│   ├── autoload/          # 全局单例：GameManager / EventBus / SaveSystem / DevTestHub
│   ├── bullet/            # 弹幕系统：Bullet / BulletPool / BulletPatterns / BulletEmitter
│   ├── player/            # 玩家控制器 PlayerController
│   ├── ui/                # UI（Hud 等）
│   ├── scenes/            # 场景脚本（Boot.cs）
│   └── test/              # 测试：TestProbes / TestSuite
├── tests/                 # 测试场景（TestSuite.tscn）
├── tools/                 # 构建 / 运行 / 打开编辑器脚本
└── docs/                  # 开发纪律文档（见 AGENTS.md 必读列表）
```

## 核心系统

### 弹幕对象池（BulletPool）
弹幕手感与性能的基础设施：预热分配 + Spawn/Release 复用（绝不高频 new），出屏自动回收，峰值扩容兜底。见 `scripts/bullet/BulletPool.cs`。

### 弹幕 Pattern 数学（BulletPatterns）
纯函数弹型算法库：Aimed（瞄准）/ Spread（扇面）/ Ring（圆环）/ Spiral（螺旋），无状态、可单测。见 `scripts/bullet/BulletPatterns.cs`。

### 事件总线（EventBus）
轻量事件解耦：低频跨系统事件走 `Dispatch`；只触发一次的事件走 `TriggerOnce`（读档不重复）；弹幕 hotpath 不走总线（用对象池内直连）。见 `scripts/autoload/EventBus.cs`。

### 测试中枢（DevTestHub）
探针（只读状态检查）+ 种子（状态预置）分离，headless 可跑。见 `docs/dev/TestSystem.md`。

## 技术基建约定

- **自定义 user 目录**：`project.godot` 已配置 `config/use_custom_user_dir`，`user://` 落在 `AppData/Roaming/magicThunder`（纯 ASCII，避开项目名特殊字符）。测试脚本额外把 `APPDATA` 重定向到工程内 `.godot/userhome`，避免沙箱拦截。
- **数据驱动**：角色/数值用 `.tres` 资源配置，运行时通过 GD.Load 读取。
- **存档契约**：`SaveVersion` 是格式红线，改前必读 `docs/dev/SaveSystem.md`。
