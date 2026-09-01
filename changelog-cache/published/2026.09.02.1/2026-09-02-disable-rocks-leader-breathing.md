# 关闭蓝色洛克斯领袖卡动态呼吸效果

- 日期：2026-09-02
- 分类：优化
- 影响范围：OP17-039「洛克斯·D·吉贝克」领袖卡视觉效果
- 状态：已完成

## 玩家可见说明

- 蓝色洛克斯领袖卡不再显示人物起伏、辉光、扫光和边框脉冲，卡面改为稳定的静态显示。

## 技术说明

- 从领袖呼吸效果配置表中移除 OP17-039，保留通用卡面渲染和呼吸效果扩展能力不变。
- 新增回归测试，覆盖普通画面、带缓存版本参数的画面和异画均不启用呼吸动画。

## 验证结果

- `node --disable-warning=MODULE_TYPELESS_PACKAGE_JSON --test tests/leader-breathing-effects.test.mjs tests/game-layout.test.mjs tests/card-item-mobile-gesture.test.mjs`：通过 15 项，失败 0 项。
- `npm run build`：通过，Next.js 生产构建和 TypeScript 检查均成功。
- 浏览器实际检查：桌面 `1280×800`、手机竖屏 `390×844` 和 `360×780` 下，洛克斯卡面均完整可见、无横向溢出，呼吸动画叠层数量均为 0；对局页竖屏自动旋转横屏画布由布局回归测试覆盖。
