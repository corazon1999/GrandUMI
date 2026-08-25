# 修复 OP10-063 对 GERMA 复合领袖特征的识别

- 日期：2026-08-26
- 分类：修复
- 影响范围：OP10-063 登场时效果、GERMA 领袖条件与卡组顶检索
- 状态：已完成

## 玩家可见说明

- OP10-063 现在会在领袖特征中包含“GERMA”时正确发动；使用《温思默克家/GERMA 66》领袖也能确认卡组顶5张并将符合条件的卡加入手牌。

## 技术说明

- G819：仅将该卡的领袖条件由完整特征精确匹配改为特征包含匹配；顶卡候选原有的 GERMA 包含匹配保持不变。
- 条件不满足时仍不会创建检索提示或移动卡牌，避免扩大到不含 GERMA 的领袖。

## 验证结果

- 新增真实 DSL 效果链回归，以 OP06-042 领袖和 OP10-064 顶卡先复现无提示，再验证修复后生成唯一检索提示、候选可选且卡牌从卡组进入手牌。
- `dotnet test 服务端WebSocket.Tests/GrandUMIServer.Tests.csproj --no-restore --filter "FullyQualifiedName~G811|FullyQualifiedName~G819"`：2项通过。
