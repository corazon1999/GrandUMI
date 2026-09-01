# Leader 榜固定场次档位

- 日期：2026-09-02
- 分类：优化
- 影响范围：Leader 排行榜、对阵详情与对阵一图流
- 状态：已完成

## 玩家可见说明

- Leader 榜的场次筛选改为独立的 100、300、500、1000、3000 场和全部六档；筛选范围只由近 7 天、近 30 天或全部时间决定，切换统计周期不再自动改变场次门槛。

## 技术说明

- 前端筛选状态、请求参数、详情缓存键与一图流文案统一使用固定场次档位，默认选择 500 场；旧版浏览器存储的 `relaxed`、`standard` 会迁移为 100、500 场。
- 服务端按固定档位筛选对应周期内的 Leader 场次，并让排行榜、单 Leader 对阵和完整对阵矩阵共用同一筛选来源与缓存口径。
- 旧客户端继续支持 `relaxed`、`standard` 组合档位，并在回包中保留旧档位值，避免旧版请求关联和页面状态失效。

## 验证结果

- `node --disable-warning=MODULE_TYPELESS_PACKAGE_JSON --test tests/leader-filter-tiers.test.mjs tests/leader-matchup-matrix.test.mjs tests/leader-matchup-matrix-export.test.mjs tests/mobile-leaderboard-scroll.test.mjs`：通过，19 项测试全部成功。
- `npx tsc --noEmit`：通过，无 TypeScript 错误。
- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj --filter FullyQualifiedName~LeaderStatsStoreTests --no-restore`：通过，37 项 Leader 统计专项测试全部成功。
- `dotnet test .\服务端WebSocket.Tests\GrandUMIServer.Tests.csproj --no-restore`：通过，1877 项成功、2 项平台相关测试跳过、0 项失败。
- 本地浏览器检查 `1440×900`、`390×844`、`360×780`：六档完整可见，手机竖屏为 3×2 排列，无横向溢出，所有档位按钮高度均为 44px；切换统计周期后场次档位保持选中。
