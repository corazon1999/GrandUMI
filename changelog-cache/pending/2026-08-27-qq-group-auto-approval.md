# QQ 群加群申请自动审批

- 日期：2026-08-27
- 分类：新增
- 影响范围：QQ 群机器人、玩家加群申请
- 状态：已完成

## 玩家可见说明

- 玩家在加群问题中填写邀请人 QQ 后，机器人会实时确认邀请人是否仍在群内；邀请人在群则自动同意申请，不在群或答案无效则直接拒绝。
- 群成员查询暂时失败时，申请会保持待审批，不会因接口故障被误同意或误拒绝。

## 技术说明

- 新增独立、默认关闭且必须显式填写目标群的自动审批配置，只处理 OneBot `request/group/add` 事件，成员邀请等其他申请事件保持原行为。
- 邀请人答案只从申请 `comment` 的纯 QQ 或受限问答包装中提取唯一号码，拒绝多号码、无关文字、申请人本人和机器人自身。
- 使用 `get_group_member_list` 并强制 `no_cache=true` 权威核验，通过事件原始 `flag` 调用 `set_group_add_request`；查询或动作失败不记为成功，已成功事件有界去重，已在群的申请人不会再次操作过期申请。
- 同一群同时配置入群后邀请人验证时，申请阶段审核结果会作为持久登记授权；玩家入群后继续完成邀请人登记，且群消息不能改写已审核的邀请人 QQ。

## 验证结果

- `py -m unittest qq-bug-bot/tests/test_group_add_auto_approval.py qq-bug-bot/tests/test_member_verification.py`：30 项全部通过。
- `py -m unittest discover -s qq-bug-bot/tests -p 'test_*.py'`：106 项全部通过。
- `git diff --check`：通过。
