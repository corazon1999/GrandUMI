# OP12-037 可选择活跃咚目标

- 日期：2026-08-26
- 分类：修复
- 影响范围：卡牌效果 / OP12-037「鬼气 九刀流 阿修罗 拔剑 亡者游戏」
- 状态：已完成

## 玩家可见说明

- OP12-037 的【主要】效果支付横置 3 张我方咚!!的成本后，现在可以从对方的活跃角色与活跃咚!!中合计选择最多 2 张转为休息状态；已处于休息状态或其他不合法目标不会出现在选择列表中。

## 技术说明

- 将原先“先选择角色、再确认自动横置咚”的分段结算改为单一混合目标提示，以唯一实例 ID 同时承载对方合法活跃角色与活跃咚候选。
- 通过现有 `choiceCards` 与 `donChoices` 提示数据让前端同列展示两类目标，并排除已横置、不可被选择或不可转为休息状态的角色，以及领袖、舞台和非活跃咚。

## 验证结果

- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj --filter "FullyQualifiedName~OP12_037|Name~G715_OP12_037" --no-restore`：通过 5/5。
- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj --no-restore`：通过 1427/1427。
- `node --test tests/effect-confirm-prompt-layout.test.mjs tests/player-feedback-prompt-ui.test.mjs tests/prompt-selected-target-marker.test.mjs`：通过 21/21，覆盖旋转画布安全区、手机触控尺寸及目标实例选择状态。
