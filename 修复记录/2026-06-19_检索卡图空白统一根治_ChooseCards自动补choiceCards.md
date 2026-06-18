---
范围: 检索/选择面板部分卡牌图加载不出（统一根治）
日期: 2026-06-19
对应反馈: #61（6-17 日报）
编译: dotnet test 59/59 通过
---

## 根因（沿用前期调查）

前端 `PromptOverlay.findCardById` 反查候选卡图的来源依次是：`extra.choiceCards`（卡号映射）→ 领袖 → 双方**场上** fieldCards。因此当后端某次 `ChooseCards` **未下发 choiceCards、且候选卡在隐藏区**（卡组/废弃/生命/手牌，不在场上）时，前端反查不到 → 显示深色「CARD」占位（即"卡图加载不出"）。与已修反馈 #19（OP11-054 抽三放二无卡图）同类，只是散落在一批脚本卡里。

逐个脚本补 choiceCards 是上百处的审计活，易漏。改为**统一根治**。

## 修复：在 ChooseCards 统一出口自动补 choiceCards

`Effects/PromptSystem.cs` 的 `ChooseCards`（所有脚本 + DSL 选择的唯一实现）在已有"自动注入 sourceNumber"之后，新增自动注入 choiceCards：
- 对 `validChoices` 里每个 id，用新增的 `FindCardByIdAnyZone` 在**双方所有区域**（领袖/角色/舞台/手牌/卡组/废弃/生命）反查 CardInstance，取 `Info.Number`，组成 `choiceCards` 写入 `extra`（仅当调用方未显式传 choiceCards 时）。
- **非卡 id**（选项序号、`trigger`/`hand`/`是`/`否` 等）经 `Guid.TryParse` 失败自动跳过，不影响 ChooseOption/LifeTrigger 等非选卡 prompt。

一处改，覆盖全部脚本卡 + DSL 的选卡场景，隐藏区候选从此都有卡图。

## 安全性：不泄露隐藏区给对手

`Game/Snapshot/StateSnapshotBuilder.cs:46` 已确认：`pendingPrompt`（含 `extra`/choiceCards）**仅在 `PlayerIndex == myIdx`（选择方本人）的快照里下发**，对手快照为 null。故自动补的卡组/生命等隐藏区卡号只有选择方自己能看到，不会泄露给对手或观战。

## 残留（次要）

另一类"首屏 loadAllCards 未完成时弹检索 → getCard 暂返回 undefined"的时序竞态：窗口很短，且卡数据加载完成会触发 GamePage 子树重渲染自动刷出卡图，影响很小，未单独处理。主因（隐藏区 choiceCards 缺失）已统一根治。

> 注：本记录取代 `2026-06-19_核实误报与缺口_…md` 中将 #61 列为"未做缺口"的判断。
