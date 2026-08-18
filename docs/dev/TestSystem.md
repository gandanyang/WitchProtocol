# 自动化测试体系契约（TestSystem）

> 对齐 ReturnToStars TestSystem 的「探针 / 种子」思路，用 Godot headless 跑纯逻辑验证。
> **涉及测试 / 写探针 / 改动验证必读。**

## 一、核心概念

| 概念 | 定义 | 纪律 |
|---|---|---|
| **探针（Probe）** | 只读状态检查，返回 `bool` | **无副作用**；不写存档、不改状态 |
| **种子（Seed）** | 把游戏置于某状态 | 用现有存档/事件系统模拟真实玩家状态，**禁止直接改内存**；结束必须还原 |
| **套件（TestSuite）** | headless 入口，逐跑探针并出报告 | 退出码 = 失败数（0 通过） |

探针/种子分离了「当前状态检查」与「跳转目标预置」，避免散落调试代码。

## 二、如何运行

```powershell
.\tools\run_tests.ps1                  # 5 个默认探针：boot,event,save,pool,pattern
.\tools\run_tests.ps1 boot,pool        # 只跑指定探针
```

等价命令（不含构建）：

```bash
godot --headless --path . res://tests/TestSuite.tscn -- --probes=boot,event,save,pool,pattern
```

输出报告：`user://test_reports/latest.txt`（沙箱下在 `.godot/userhome/magicThunder/test_reports/latest.txt`，不入库）。

## 三、现有探针一览

| 探针 id | 检查内容 | 文件 |
|---|---|---|
| `boot` | 4 个 autoload 单例就位 + 有效视口 | `scripts/test/TestProbes.cs` |
| `event` | Dispatch 记日志；TriggerOnce 去重 | 同上 |
| `save` | 写→存→读往返；版本一致；Reset 备份+归位 | 同上 |
| `pool` | 对象池 spawn/release 数量守恒 + 复用 | 同上 |
| `pattern` | aimed/spread/ring/spiral 数学正确 | 同上 |

## 四、编写新探针的范式

在 `scripts/test/TestProbes.cs` 里：

```csharp
private static bool ProbeMyFeature()
{
    // 1) 只读检查，不写任何持久状态
    // 2) 失败时 return false（可先 return false 快速定位）
    return true;
}
```

注册：

```csharp
hub.RegisterProbe("myfeature", ProbeMyFeature);
```

约束：
- 探针 id 用简短小写命名。
- 探针**不得**依赖渲染结果（headless 可跑），只测纯逻辑。
- 探针失败要能自解释：宁可 `return false`，不要吞异常假装通过。

## 五、测试纪律（硬性）

1. **改动核心系统后必须跑探针**：改 autoload / 对象池 / Pattern / SaveSystem → 跑 `run_tests.ps1`，5 探针必须全绿再收工。
2. **发布构建隔离**：DevTestHub 探针仅在 headless + 显式 `--probes` 参数时执行，天然不进正式流程。新增测试入口必须保持同等级隔离（见 `AI_GUARDRAIL.md`）。
3. **种子必须还原**：任何测试种子结束要把状态 Reset 归位，不留脏存档。
4. **沙箱兼容**：写 `user://` 的测试请通过 `run_tests.ps1` 运行（已重定向 APPDATA），不要手动裸跑导致沙箱拦截。
