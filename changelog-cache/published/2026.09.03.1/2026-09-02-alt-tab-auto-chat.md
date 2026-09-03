# 对局 Alt+Tab 自动提示

- 日期：2026-09-02
- 分类：新增
- 影响范围：电脑端对局、局内聊天
- 状态：已完成

## 玩家可见说明

- 正在对局的玩家按 Alt+Tab 切出游戏时，会自动在局内发送“老板来了，等我一会”，方便及时告知对手。

## 技术说明

- 客户端先记录明确的 Alt+Tab 按键意图，再以紧随其后的页面隐藏或窗口失焦确认切出；普通 Alt 快捷键、普通 Tab、单独失焦及手机切后台不会触发。
- 每次切出只发送一次，页面重新可见或窗口重新聚焦后才允许再次触发；发送前实时校验当前为未结算的玩家对局，观战与回放不会发送。
- 复用现有局内聊天请求路径，不新增服务端协议或权威状态。

## 验证结果

- `node --disable-warning=MODULE_TYPELESS_PACKAGE_JSON --test tests/alt-tab-auto-chat.test.mjs tests/friend-chat-tabs.test.mjs`：14 项测试全部通过。
- `npx tsc --noEmit --target ES2022 --module ESNext --moduleResolution Bundler --lib ES2022,DOM --skipLibCheck src/lib/altTabAutoChat.ts`：通过。
- 全项目 `npx tsc --noEmit` 受本任务外既有 `src/components/home/AdminHexCatalogPanel.tsx:133` 定时器类型错误阻挡；本功能涉及的独立模块类型检查通过。
