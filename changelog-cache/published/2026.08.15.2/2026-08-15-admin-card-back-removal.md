# 管理员下架已发布卡背

- 日期：2026-08-15
- 分类：新增
- 影响范围：卡背广场、玩家卡背选择、管理员内容管理
- 状态：已完成

## 玩家可见说明

- 卡背广场管理员可以在热门列表中下架不适合公开展示的已发布卡背；受影响玩家会自动恢复为经典卡背。

## 技术说明

- 热门卡背列表为管理员显示独立删除入口，并提供包含作者信息的不可恢复二次确认。
- 服务端仅允许管理员删除已经审核通过的他人卡背，未发布投稿仍只能由投稿者本人删除。
- 下架时同步重置所有正在使用该卡背的玩家，并向在线会话刷新玩家数据与卡背广场。
- 补充英文和日文动态确认文本。

## 验证结果

- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj -c Release --filter FullyQualifiedName~PlayerDataStoreTests --no-restore -p:UseSharedCompilation=false`：22 项通过。
- `node --test .\opcgpro-web\tests\card-back-plaza-management.test.mjs .\opcgpro-web\tests\i18n.test.mjs`：12 项通过。
