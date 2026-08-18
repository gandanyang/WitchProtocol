# 存档契约红线（SaveSystem）

> 涉及存档改动必读。SaveSystem 是**唯一持久状态入口**，所有业务持久化必须走它。
> 实现见 `scripts/autoload/SaveSystem.cs`。

## 一、存档文件

- 路径：`user://save.json`（JSON）
- 位置：正式玩家 → `AppData/Roaming/magicThunder/save.json`；沙箱测试 → `.godot/userhome/magicThunder/save.json`（不入库）
- 唯一入口：`SaveSystem`（autoload，`SaveSystem.I` 访问）

## 二、版本红线（SaveVersion）

```csharp
public const int SaveVersion = 1;  // 当前存档格式版本
```

规则：

1. **只有破坏性结构变更才递增** `SaveVersion`（如删除字段、改字段含义、改序列化格式）。
2. **新增字段严禁升版本**：新增字段必须「可选 + 默认值兜底」（见下节），旧档读入后补默认值即可。
3. **读取任何字段先走 `Get(key, 默认值)`**，业务代码**禁止**出现 null 断言 / 强转假设字段必在。

## 三、字段兜底表（EnsureDefaults）

所有存档字段必须在此给默认值，保证读档永远拿到完整结构：

| 字段 | 默认值 | 说明 |
|---|---|---|
| `chapter` | `0` | 当前章节 |
| `player_depth` | `0.0` | 玩家深度（示例） |
| `fired_events` | `[]` | 已触发一次性事件（读档不重复） |

新增持久化字段 → **同时**在 `EnsureDefaults()` 补默认值。

## 四、清档 / 迁移纪律

- 清档：调用 `SaveSystem.Reset()`。
  - 已自动把旧档备份到 `user://save_backup_{SaveVersion}.json` **再**清空重写。
  - 已有备份会先删除再覆盖（避免堆积）。
- 迁移（升版本）：必须先备份旧档，迁移逻辑写成显式函数并加探针验证，禁止在 `Load()` 里悄悄改结构。

## 五、一次性事件（EventBus.TriggerOnce）入档

- `EventBus` 用 `TriggerOnce(id)` 做内存态去重，返回 `true` 表示本次首次触发。
- 持久化标记由 SaveSystem 的 `fired_events` 负责：事件触发后把 id 写入 `fired_events`，读档时据此恢复 `EventBus._once`，实现「读档不重复」。
- 注意顺序：**先执行副作用，再把标记入档**（对齐 ReturnToStars 事件契约）。

## 六、接口速览

| 成员 | 说明 |
|---|---|
| `SaveSystem.I` | 单例访问 |
| `SaveSystem.SaveVersion` | 格式版本（静态常量） |
| `HasSave()` | 是否有存档 |
| `Save()` | 写入存档（含 save_version） |
| `Load()` | 读取 + 兜底，损坏则拒绝并报错 |
| `Reset()` | 备份 → 清空 → 重写 |
| `Get(key, fallback)` | 读字段（不存在返回 fallback） |
| `Set(key, value)` | 写字段（内存态，需 `Save()` 落盘） |
| `Contains(key)` | 字段是否存在 |
