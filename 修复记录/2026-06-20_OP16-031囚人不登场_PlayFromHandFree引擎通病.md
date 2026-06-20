# OP16-031 KO时登场囚人：候选未过滤 + 选中确认不登场（引擎通病）

> 日期：2026-06-20（6-20 反馈）
> 现象：OP16-031（巴奇）【KO时】效果列表把全部手牌当候选（非合法单位也可选）；选中「因佩尔地狱的囚犯」确认后也没有把它登场。
> 编译：`服务端WebSocket` 目录 `dotnet build` 0 警告 0 错误。

## 卡面
OP16-031【KO时】将我方手牌中最多1张"因佩尔地狱的囚犯"登场。
- "因佩尔地狱的囚犯" = OP16-042（蓝/角色/6费）。

## 根因与修复

### 改1 — 候选未按卡名过滤（卡数据）
`服务端WebSocket/Effects/Definitions/OP16.json` OP16-031 的 Choose 用 `prompt:"OwnHand"` 且**无 filter**，任意手牌都进候选。
修：加 `filter: { "nameEquals": "因佩尔地狱的囚犯" }`。服务端 BuildCandidates 后套 BuildMatchPredicate 过滤，候选只剩 OP16-042，前端自然只显示合法卡（满足"非合法单位不显示"）。

### 改2 — PlayFromHandFree 登场失败（引擎级通病，影响所有同模式卡）
`服务端WebSocket/Effects/Dsl/DslInterpreter.cs` 的 `case "PlayFromHandFree"` 原用 `int owner = FindOwner(s, target)`，而 `FindOwner`（同文件 :1260）**只搜场上**（Leader/Characters/StageCard），不搜手牌 → 手牌卡恒返回 -1 → `if(owner>=0)` 不成立 → 静默不登场。
而 `AtomicOps.PlayFromHandFree(playerIdx, card)` 本就自己从该玩家手牌移除卡，正确 owner 应是 `ctx.OwnerIndex`（登场己方手牌卡，owner 恒为效果控制者）。
修：`int owner = ctx.OwnerIndex;`，去掉 `if(owner>=0)` 包裹。

> ⚠️ 这是公共 op 的 bug，**同时修好 OP14-014 等所有「Choose 手牌 → PlayFromHandFree」模式的卡**（此前都是选完确认不登场）。`PlayFromHandFree` op 语义上永远登场己方手牌，改为 ctx.OwnerIndex 更正确、无回归风险。

## 同模式卡（已随改2修复，无需逐张改）
OP14 中 `op:"PlayFromHandFree"` 共 7 处（OP14.json:144/292/439/512/582/642/1378），均为登场己方手牌，此前同样受 FindOwner -1 影响。
