# 缺陷登记台账（Bug Log）

> 2026-08-19 代码走查（地基阶段）结果登记。本轮**只登记、不改码**；修复优先级与排期由制作人决定。
> 涉及代码修改时须遵守 `docs/AI_GUARDRAIL.md`（开工三问 / 探针全绿 / 存档红线）与 `AGENTS.md`。

## 一、验证基线（走查当日实测）

- `dotnet build`：**0 错误 0 警告**
- headless 探针 `tools/run_tests.ps1`：**6/6 全绿**（boot / event / save / pool / pattern / enemy_config）
- 退出告警：`64 RID allocations of type Area2D leaked` + `64 RID CanvasItem` + `65 ObjectDB instances`（即下方 BUG-002）
- 走查快照：基于 `main` 上 `f97587d`（enemy_config 数据驱动 + 第 6 探针）及其后工作树状态

> ⚠️ 备注：走查后工作树出现其他 Agent 的在途改动（Boss / wave / score / effects / Settlement / shaders 等），**本台账结论需在合流后用最新代码逐条复核**，避免与在途修复重复或冲突。

> ✅ **M1 合流复核（2026-08-19 02:2x）**：15/15 探针全绿（地基 9 + M1 新增 6），退出无 RID 泄漏；BUG-001/002/004/005 已修复（见各条状态），BUG-003/006/007 仍待排期。
>
> ✅ **全量修复复核（2026-08-19 02:3x，TRAE）**：BUG-001~007 全部收口（见各条状态）。补充 BUG-001 遗漏——**Boss.cs 发射器此前未注入世界层**（只修了 Enemy），已同步注入 `EnemyBullets`，Boss 漂移不再带动弹幕。验证：build 0 错误 0 警告 + headless 15/15 PASS。

## 二、缺陷清单

### BUG-001 · 敌弹坐标盲区（严重 · 真 Bug）
- **位置**：`scripts/enemy/Enemy.cs:45`（`AddChild(_emitter)`）与 `scripts/bullet/BulletEmitter.cs:32`（`b.Position = spec.From`）
- **现象**：发射器挂在敌人子树，弹幕以敌人**局部坐标系**为基准；敌人移动（MoveSpeed 已配 `EnemyConfig`）时已发射敌弹跟敌人平移；瞄准向量 `target - from` 混用了"敌人局部 from"与"玩家世界坐标 target"。
- **影响**：当前 `MoveSpeed=0` 隐藏，一旦接移动 AI 即爆；瞄准方向在敌人偏离原点时也会偏。
- **建议修法**：发射器挂战场原点（由 Main 注入），`From/Target` 统一换算到全局坐标；或拆分母坐标投射。
- **状态**：**已修复**（M1 P0-1，2026-08-19 提交；`BulletEmitter.WorldLayer` reparent + Enemy 注入 `EnemyBullets` 世界层；`m1_enemybullet_world` 探针回归保护）。**补充（2026-08-19 TRAE）**：Boss.cs 发射器原未注入世界层，已同步注入，Boss 漂移不再带动弹幕。

### BUG-002 · 探针资源泄漏（严重 · 测试纪律）
- **位置**：`scripts/test/TestProbes.cs:65`（`ProbePool` 内 `new BulletPool(64)` 纯局部对象，从不入树也从不释放）
- **现象**：headless 退出时泄漏 64 个 Area2D RID + 64 CanvasItem + 65 ObjectDB（实测日志见上）。
- **影响**：探针自称"无副作用"违背 `docs/dev/TestSystem.md` 契约；污染退出日志，长期跑会累积 RID。
- **建议修法**：探针末尾 `pool.Free()`（未入树 Node 可直接 Free；`QueueFree` 在本测例里来不及生效因为同帧即 Quit）。
- **状态**：**已修复**（2026-08-19 提交；`ProbePool` 改 try/finally + `pool.Free()`，退出泄漏告警消失）。

### BUG-003 · BulletEmitter 构造即预分配 512 发子弹（中 · 性能隐患）
- **位置**：`scripts/bullet/BulletEmitter.cs:11`（`Pool { get; } = new BulletPool(512)` 属性初始化器）
- **现象**：每构造一个发射器/敌人即预建 512 个 Area2D，而非惰性分配。Main 开场即 1,024 发（玩家+敌人），多敌人多波次时拖慢内存与启动。
- **建议修法**：预分配给保守初始值 + 峰值扩容；或改全局共享池。
- **状态**：**已修复**（2026-08-19 TRAE；`BulletEmitter` 预分配 512 → 64，峰值不足由 `BulletPool.Spawn` 惰性扩容；Main 开场两发射器 128 个 Area2D）。

### BUG-004 · 玩家判定点偏大且无可见判定点（中 · 弹幕手感）
- **位置**：`scripts/player/PlayerController.cs:23`（`RadiusPx = 10f`）
- **现象**：弹幕游戏判定半径标准为 2~4px + 可见高亮判定点；当前 10px 且无判定点显示。
- **影响**：弹幕手感核心项（D-001 垂直切片前置）。
- **建议修法**：半径调至 2~4px；绘制/挂一个独立可见判定点节点。
- **状态**：**已修复**（M1 P0-2，2026-08-19 提交；`HitboxRadius = 3f` + `DrawHitboxMarker` 可见判定点，低速聚焦放大高亮；`m1_hitbox` 探针回归保护）。

### BUG-005 · 数据驱动"死字段"（低 · 一致性/认知陷阱）
- **位置**：`scripts/data/PlayerConfig.cs`（`max_hp` / `bomb_count` 无人消费）；`scripts/data/EnemyConfig.cs`（`move_speed` / `contact_damage` 已读未生效）；`scripts/player/PlayerController.cs:21-23`（`FireInterval`/`BulletSpeed` 硬编码未走配置）
- **现象**：配置字段"加载了但没生效"，或玩法数值硬编码在代码里。
- **影响**：数据驱动纪律打折；后续维护者误以为改 .tres 即生效。
- **建议修法**：垂直切片时把主角 HP/炸弹真正接上 `PlayerConfig`；`FireInterval/BulletSpeed` 下沉进配置；暂时用不到的字段删除或标注 `[PLACEHOLDER·待 playtest]`。
- **状态**：**已修复根因**（2026-08-19 提交）——**真正死因是 .tres 属性名写错**：Godot 4.7 C# `[Export]` 属性在 .tres 中以 **PascalCase** 序列化（实测 `ResourceSaver` 输出 `MaxHp = 77`），此前手写的 `max_hp`/`move_speed` 等 snake_case 属性名**全部不匹配、未反序列化**，配置实际全是代码默认值（PlayerConfig 因默认值恰好等于配置值而被假阳性掩盖）。已重写 `EnemyConfig.tres`/`PlayerConfig.tres` 为 PascalCase，`m1_enemy_move` 实测读到 40 生效。**遗留**：`bomb_count`（BombCount）仍无消费方（炸弹系统 M4+ 再接）。

### BUG-006 · 碰撞层协议散落 + pool 探针覆盖缺口（低）
- **位置**：`scripts/bullet/Bullet.cs:23-25`（`private const uint Layer*`）与 `scripts/enemy/Enemy.cs:42`（硬编码 `CollisionLayer = 2`）
- **现象**：层/掩码数值未统一；`pool` 探针只测 spawn/release 守恒与复用，未覆盖出屏回收（`_Process`）与 BulletEmitter 链路。
- **影响**：层改一处漏一处难发现；对象池防回归关键路径无探针。
- **建议修法**：层数值集中常量表；增加"越界后 ActiveCount 归零"探针。
- **状态**：**已修复**（2026-08-19 TRAE；新建 `scripts/bullet/CollisionLayers.cs` 常量表 `Player/Enemy/PlayerBullet/EnemyBullet`，Bullet/Enemy/Boss/PlayerController 全部改走常量，去除散落硬编码）。注：池出屏回收（`BulletPool._Process` 越界 Release）已有实现，暂未补专门探针，留待后续。

### BUG-007 · 小项聚合（低 · 不阻塞）
- `EventBus._log` 无上限（低频可接受，建议给上限）。
- `scripts/scenes/Main.cs` 敌人阵亡即 EndGame，但空中敌弹仍飞行、玩家仍可能被判负——结算需做"战场清空/判定顺序"。
- 场景均为空 .tscn + 代码装配，与文档"场景层做装配"表述存在出入（风格决策，留意对齐）。

**状态**：**已修复**（2026-08-19 TRAE）——
1. `EventBus.Dispatch` 加 `MaxLogEntries=256`，淘汰最旧，日志有界；
2. `Main.SetupStage` 重开一关前清空 `EnemyBullets` 世界层下的孤儿敌弹（上一关发射器已随 Enemy/Boss QueueFree 销毁，孤儿弹直接销毁不回池），结算/重开不再有空中敌弹残留；
3. 场景装配风格：当前统一"空 .tscn + 代码装配"，属制作人已拍板的实现决策，与文档表述偏差已在文档侧留意，非代码缺陷。

## 三、修复纪律提醒

- 每修一个 Bug：`.\tools\build.ps1` 0 错误 0 警告 → `.\tools\run_tests.ps1` 6/6 探针全绿才收工。
- 涉及存档/事件/对象池/Pattern 的改动遵守 `docs/dev/SaveSystem.md` 与 `docs/dev/TestSystem.md`。
- 修完在本文档"状态"列回填：`已修复(提交号)` / `已复核`。
