# QQ 群成员邀请好友入群自动审批

- 日期：2026-08-27
- 分类：新增
- 影响范围：QQ群机器人加群请求自动审批
- 状态：已完成

## 玩家可见说明

- 在自动审批目标群内，群成员邀请好友入群且该邀请需要管理员审核时，机器人会直接同意，不再受空白或无效问题答案影响。
- 好友邀请机器人自身加入其他群时仍不会自动同意，避免机器人被未经授权拉群。

## 技术说明

- 按 NapCat 事件语义仅处理 OneBot 群请求 `sub_type=add`；`sub_type=invite` 表示邀请机器人自身入群，继续完全忽略。
- 每个目标群 `add` 事件先通过 `get_group_member_list(no_cache=true)` 取得权威成员列表。事件 `user_id` 已在群时，视为群成员邀请好友或已处理事件，直接使用事件原始 `flag`、`sub_type=add`、`approve=true` 审批且不解析 `comment`；`user_id` 不在群时，才按原规则解析答案，并复用已取得的成员列表核验所填邀请人。
- 仅在 OneBot 明确确认审批成功后写入最多 2048 条的进程内去重缓存；成员查询失败、审批动作失败、取消或过期 `flag` 均不写入，允许同一事件后续重试。
- 自动审批仍只对显式配置的目标群生效，原有普通申请拒绝规则及与入群后二次验证的互斥规则保持不变。

## 验证结果

- `py -m unittest tests.test_group_add_auto_approval`：通过，共 13 项测试。
- `py -m unittest discover -s tests -p "test_*.py"`：通过，共 108 项测试。
- `git diff --check -- qq-bug-bot/bot.py qq-bug-bot/tests/test_group_add_auto_approval.py qq-bug-bot/README.md`：通过。
- 已核对 NapCat 当前源码：邀请机器人自身映射为 `invite`；群成员邀请好友且需管理员审核映射为 `add`，其 `user_id` 为群内邀请人。
