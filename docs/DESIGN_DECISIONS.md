# 制作人决策库（DESIGN_DECISIONS）

> 已拍板项，Agent 不得推翻。新增决策需制作人拍板后追加，格式：`D-编号（日期）标题`。

## D-001（2026-08-19）地基阶段 → 第一章垂直切片

**决策**：当前项目处于「地基阶段」，工程骨架 + 核心系统 + 测试体系已就位（5/5 探针 PASS）。下一阶段目标是做出一条**可完整游玩的弹幕战斗闭环**（垂直切片），而非继续堆系统。

**理由**：从「加功能」转向「证明好玩」。先让玩家玩到一条完整的弹幕战斗体验，再扩展内容。

**约束**：垂直切片优先保证：弹幕手感（对象池 + Pattern）→ 玩家闪避 → 敌方弹幕 → 记忆碎片收集 → 一个情感收束点。新增系统前先过「新增系统检查清单」（见 `docs/开发约束与架构入口.md`）。

## D-002（2026-08-19）技术栈：Godot 4.7 + C#

**决策**：本工程采用 **Godot 4.7.1 mono + C#/.NET 8**（区别于参考项目 ReturnToStars 的 Phaser 3 + TS）。

**理由**：二次元弹幕 RPG 需要稳定高性能的 2D 渲染与对象池热路径，C# 静态类型更适合弹幕数学与数据驱动；Godot 4.7 为 LTS 前的成熟版本。

**约束**：不改引擎 / 不引入后端。渲染 Forward Plus（桌面）。

## D-003（2026-08-19）自定义 user 目录名

**决策**：`project.godot` 配置 `config/use_custom_user_dir=true` + `custom_user_dir_name="magicThunder"`，`user://` 落在 `AppData/Roaming/magicThunder`。

**理由**：项目名「Witch Protocol · 魔女协议」含 `·` 与空格，会污染 user:// 路径，且沙箱/日志写权限不稳。ASCII 目录名更稳、存档路径更短。

**约束**：正式发布若要改存档根路径，需在 `docs/dev/SaveSystem.md` 登记并评估迁移。

## D-004（2026-08-19）沙箱测试时 user:// 重定向

**决策**：TRAE 沙箱下，`tools/run_tests.ps1` 把 `APPDATA` 重定向到工程内 `.godot/userhome`，让 `user://`（存档/日志/测试报告）落在工程内、不入库。

**理由**：沙箱禁止写系统 AppData，导致 save 探针失败；重定向后 5 探针全绿且无污染。

**约束**：`.godot/` 已在 `.gitignore`，重定向目录不会入库。正式玩家的 `user://` 仍是 `AppData/Roaming/magicThunder`，不受影响。
