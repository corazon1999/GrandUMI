---
卡号: OP12-028（波及 OP12-080、OP12-108、OP12-047）
日期: 2026-06-08
现象: 检索类效果（确认卡组顶 N 张、公开符合条件的 ≤M 张加入手牌）发动时，不符合条件的牌在弹窗里看不到卡图，只显示符合条件的少数几张。
根因: 这类脚本约定 `extra.choiceCards` 应下发"确认到的**全部**顶牌"（让玩家看全），`validChoices` 只给可选子集；但部分早于该约定写的旧脚本把 `choiceCards` 错误地喂成了过滤后的候选列表（`candidates`/`events`/`navy`/`targets`），导致客户端拿不到不符合条件的牌、无从显示。
修复: 把这些脚本里 `["choiceCards"] = <候选变量>` 改为 `["choiceCards"] = top`（确认到的全部顶牌）。
  - OP12_028_Hiyori.cs：candidates → top
  - OP12_080_Baratie.cs：events → top
  - OP12_108_Rosinante.cs：targets → top
  - OP12_047_Sengoku.cs：navy → top（其前一处 me.Hand 是弃牌步骤，正确，未动）
  另外客户端 PromptOverlay 把不可选牌的 `grayscale/opacity` 去掉，改为正常显示卡图 + "不可选"角标。
波及卡牌: 扫描了所有含 `Deck.Take` 的 look-top 脚本，确认只有上述 4 个误用候选；其余（OP05-069、OP06-003、OP07-013/039/077、OP08-100/110、OP11-099、OP13-016/113、OP16-067 等）以及 DSL 的集中实现 LookTopReveal 本就发 `top`，无碍。
预防: 写"确认顶N张公开M张"检索脚本时，`choiceCards` 永远 = 确认到的全部牌（top），`ChooseCards` 的候选 id 列表 = 可公开子集。新增此类卡需对照本条自查。
