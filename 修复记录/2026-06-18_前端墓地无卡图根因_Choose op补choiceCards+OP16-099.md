---
卡号: 通用根因(Choose op) + OP16-099 实修
日期: 2026-06-18
对应反馈: #82 OP16-099 查询墓地不出现卡图
---

## 根因：Choose op 选"不在场上"的卡时不下发 choiceCards → 前端无卡图

**前端机制**（PromptOverlay.tsx findCardById）：候选卡 id 反查卡图时，优先用 `extra.choiceCards` 的 number，
否则退回**场上** fieldCards/领袖。**废弃区/卡组的卡不在场上**，所以这类候选必须靠后端下发 choiceCards 才有图。

**后端缺口**：`DslInterpreter` 的 `Choose` op（593行）调 ChooseCards 时**没带 choiceCards**（仅传候选 id）。
对比 `PlayCharFromTrash`(950)、`PlayCharFromHand`(881) 都带了。故凡用 `Choose` + 废弃区/卡组类 prompt
（如 OP16-099 的 `OwnTrashCharacterCostLe6`）选卡，前端 findCardById 在场上找不到 → 卡图空白。

## 修复

### 1. 通用修复（治本，修所有同类卡）
`Choose` op 给 ChooseCards 加 `choiceCards = candidates{id,number}`。前端 findCardById 优先用此映射，
对场上候选也等价（同样 getCard(number)），**纯增益无害**。一次修掉所有"Choose 选废弃区/卡组卡无图"。

### 2. OP16-099 改用 PlayCharFromTrash（顺带补全 filter）
原 `main.then`：MillTop5 → Choose `OwnTrashCharacterCostLe6` → PlayFromTrash。
改为：MillTop5 → `PlayCharFromTrash filter:{keywordContains:"和之国", originalCostLte:6}`。
PlayCharFromTrash 本就带 choiceCards（有图），且 filter 补上了原 prompt 可能漏的《和之国》特征过滤。
（counter 段领袖+3000 不变。）

## 验证

- `dotnet test` → **59 通过 0 失败**（Choose op 通用改动未破坏任何现有行为）。
- `_eb_validate` 111 张全合法。需重启后端生效。
- 实机抽验：发动 OP16-099（6咚→卡组顶5张废弃→从废弃区选《和之国》角色登场），选卡面板应显示卡图；
  其它"从废弃区/卡组 Choose 选卡"的卡也应一并恢复卡图。
