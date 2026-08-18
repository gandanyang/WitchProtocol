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
>
> ⚠️ **二轮走查快照（2026-08-19 02:5x 起，本次会话）**：工作树此时含 `e70e5f7`（M1 合流）+ 在途改动（音频 SfxPlayer/gen_sfx.py、FeedbackSystem、DeathSequence、CollisionLayers、remove_white shader 等），且**仍在被并行 Agent 实时编辑**——下方 BUG-008~012 基于该快照，**行号/代码可能已随在途编辑漂移，合流后需复核**。
> 二轮实测：`dotnet build` **0 错误 0 警告**；`tools/run_tests.ps1` **18/18 全绿**；退出**无 RID 泄漏**（BUG-002 复验通过）。配置侧：`EnemyConfig.tres` / `PlayerConfig.tres` 已是 PascalCase（BUG-005 修复生效），但 **`BossConfig.tres` 仍是 snake_case**（见 BUG-011）。

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

### BUG-008 · 小怪阵亡后"孤儿弹" Recycle 指向已释放池（高 · 真实缺陷，P0-1 修复的边角）
- **位置**：`scripts/bullet/BulletEmitter.cs:44-51`（reparent 到世界层）与 `scripts/enemy/Enemy.cs:74`（`QueueFree`）的交互
- **现象**：小怪被击杀 → `Enemy.QueueFree()` 连同其发射器/`BulletPool` 一起销毁；但**已 reparent 到 `EnemyBullets` 世界层的在飞敌弹存活**，其 `Recycle = Pool.Release` 仍指向**已释放的池**。若该子弹命中玩家（`OnBodyEntered → RecycleSelf`）或命中后回收，会对已释放 GodotObject 调用方法 → Godot C# 抛「访问已释放实例」错误。
- **影响**：多怪波的常态路径（4/6/8 只逐只阵亡）极易触发；即使不命中，由于原池的 `_Process` 已随池销毁，**出屏回收永远不会执行——波次内幽灵弹不回收**，只在 `Main.SetupStage`（重开关卡）才全清。当前 `m1_enemybullet_world` 探针只验证"子弹进了世界层"，未覆盖此边角。
- **建议修法**：a) 子弹命中/回收前判 `IsInstanceValid(Recycle 目标)` 或让 Enemy/Boss 阵亡时把世界层内自己派发的子弹一并销毁；b) 或池被释放前把在飞子弹的 `Recycle` 解绑；c) 至少补一条探针：击杀敌人后推进若干帧，断言世界层无该池子弹残留且不报错。
- **状态**：**已修复**（2026-08-19 WorkBuddy 三轮）——`Bullet.RecycleSelf` 回收前校验 `GodotObject.IsInstanceValid(Recycle.Target)`，无效则 `QueueFree` 自毁（不抛"访问已释放实例"）；`Bullet._PhysicsProcess` 对孤儿弹（Recycle 目标无效）出屏自毁，防幽灵弹无限飞行。`m1_orphan` 探针回归（池释放后 RecycleSelf 不崩）。

### BUG-009 · 战败演出后相机 Zoom 未复位（中 · 视觉/UX）
- **位置**：`scripts/effects/FeedbackSystem.cs`（`ZoomTo` 只设动画，不提供复位）与 `scripts/scenes/DeathSequence.cs:70`（拉远到 1.45）
- **现象**：战败演出 `ZoomTo(1.45)` 结束后，`Camera2D` 的 Zoom 停留在 1.45；`Main.SetupStage`/任何处都没有复位到 1.0。
- **影响**：玩家战败过一次后，重开关卡战场持续放大显示（非预期取景），直到下次再触发 ZoomTo/重启进程。
- **建议修法**：`SetupStage`（或 `RestoreForStage`）前调用新增的 `FeedbackSystem.ResetZoom()`（把 Zoom 置回 One 并清 `_zoomT`）。
- **状态**：**已修复**（2026-08-19 WorkBuddy 三轮）——`FeedbackSystem.ResetZoom()` 新增；`DeathSequence.Finish()`（演出结束、结算出现前）调用复位，重开战场不再持续放大。

### BUG-010 · 战败重开后自机 Position / Rotation / IsDying 未复位（中 · 视觉/状态残留）
- **位置**：`scripts/player/PlayerController.cs` `RestoreForStage()`（只回血/清无敌）与 `ApplyDeathFall()`（坠落改 `Position`/`Rotation`）`DeathSequence` 驱动
- **现象**：战败演出把自机位置推离（下坠）并持续 `Rotation += 0.9*dt`；`RestoreForStage` 不重置 `Position`/`Rotation`/`IsDying`。重开后：
  - 自机在坠落残余位置重生（会被边界钳制到**屏幕最底边**，而非出生点 `center+(0,120)`）；
  - sprite 残留明显旋转角；
  - `IsDying` 残留 `true`（当前无代码消费，属潜在隐患）。
- **影响**：战败→重开，开局自机位置/姿态异常，破坏手感与节奏。
- **建议修法**：`RestoreForStage` 补充 `Position`（由 Main 重置到出生点）、`Rotation = 0`、`IsDying = false`、`_fallVelocity = Vector2.Zero`。
- **状态**：**已修复**（2026-08-19 WorkBuddy 三轮）——`RestoreForStage` 已重置 `Rotation/IsDying/_fallVelocity/_blinkTimer/_hitFlashTimer`（前一轮 M1 反馈层已含）；`DeathSequence.Finish()` 额外把 `Position` 复位到出生点 `PlayfieldSize/2+(0,120)`（与 Main.SetupStage 一致）。`deathfall` 探针回归。

### BUG-011 · BossConfig.tres 属性名仍是 snake_case，配置静默不生效 + 探针无区分度（高 · 数据驱动失效，BUG-005 同类）
- **位置**：`data/BossConfig.tres`（`max_hp = 60`、`move_speed = 40.0`、`bullet_speed = 160.0`、`ring_count = 16`…均为 snake_case）
- **现象**：按 BUG-005 已实证的规则（Godot 4.7 C# `[Export]` 在 .tres 中以 **PascalCase** 序列化；snake_case 不反序列化），`BossConfig.tres` 的字段**全部未生效**，Boss 静默跑代码默认值（`MaxHp=50 / MoveSpeed=30 / BulletSpeed=200 / RingCount=12 / RingInterval=2.0 / SpiralCount=6 / SpiralInterval=0.7 / AimedInterval=3.0`），与制作人配置（60/160/16/1.6/8/0.5/2.2）不符。
- **探针盲区**：`ProbeBossConfig` 断言 `boss.MaxHp == cfg.MaxHp`——当 .tres 未反序列化时 `cfg` 取默认值 50，与代码默认 50 **恒等 → 探针空转通过**，无法提供区分度。`EnemyConfig`/`PlayerConfig` 已重写为 PascalCase 生效，**仅 Boss 遗漏**。
- **影响**：Boss 战数值与设计不符且无人察觉；若后续调参只改 .tres 会白改。
- **建议修法**：把 `BossConfig.tres` 键名重写为 PascalCase（如 `MaxHp = 60`）；并把 `ProbeBossConfig`/`ProbeEnemyConfig` 改为「显式区分度」——断言 `cfg.MaxHp != 代码默认值`（哨兵值），杜绝空转。
- **状态**：**已修复**（2026-08-19 WorkBuddy 三轮）——`BossConfig.tres` 全部键名重写为 PascalCase（MaxHp=60/MoveSpeed=40/BulletSpeed=160/RingCount=16/RingInterval=1.6/SpiralCount=8/SpiralInterval=0.5/AimedInterval=2.2）；`ProbeBossConfig` 加哨兵 `cfg.MaxHp == 50 → false`、`ProbeEnemyConfig` 加哨兵 `cfg.MaxHp == 3 → false`（杜绝默认值空转）。`boss_config`/`enemy_config` 探针现具备真区分度。

### BUG-012 · 小项聚合（低 · 不阻塞）
- `Engine.TimeScale` 由 `FeedbackSystem.HitStop` 全局置 `0.05`，无复位兜底：死亡/胜利瞬间暂停时，`Driver`（GameManager 下、可暂停）被暂停 → 恢复后仍需约 1s（缩放时间）才还原 1.0；`Main.SetupStage` 未显式 `Engine.TimeScale = 1f`。建议 SetupStage 开头强制复位。
- `EnemyConfig` 已开 `MoveSpeed=40`（小怪追玩家），但接触伤害未落地：Enemy 是 Area2D、`CollisionMask=0`、无 overlap 处理，`ContactDamage` 配置了也不生效——小怪会"贴上"玩家却无伤害反馈（若属占位可标注 `[PLACEHOLDER]`）。
- 探针风格注意：凡"代码默认值 == 配置文件值"的探针都易空转，统一用哨兵值做区分度（见 BUG-011）。

**状态**：**部分已修复**（2026-08-19 WorkBuddy 三轮）——
1. TimeScale 复位：`DeathSequence.Finish()` 演出结束显式 `Engine.TimeScale = 1f`（兜底，不依赖 SetupStage）；
2. 接触伤害落地：`Enemy` 的 `CollisionMask = CollisionLayers.Player` + `BodyEntered` → 撞到玩家 `Dispatch("player_hit")` + 自身 `QueueFree`（防贴脸无反馈）；`m1_enemy_contact` 探针回归；
3. 哨兵纪律：`ProbeBossConfig`/`ProbeEnemyConfig` 已加哨兵断言（见 BUG-011）。

## 三、修复纪律提醒

- 每修一个 Bug：`.\tools\build.ps1` 0 错误 0 警告 → `.\tools\run_tests.ps1` 6/6 探针全绿才收工。
- 涉及存档/事件/对象池/Pattern 的改动遵守 `docs/dev/SaveSystem.md` 与 `docs/dev/TestSystem.md`。
- 修完在本文档"状态"列回填：`已修复(提交号)` / `已复核`。
