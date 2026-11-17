# OP18 中文官方卡图更新

- 日期：2026-11-17
- 分类：优化
- 影响范围：OP18 卡牌图鉴、牌组编辑器和对战卡面
- 状态：已完成

## 玩家可见说明

- OP18 卡牌现在使用官网最新的简体中文卡图，包含全部基础卡图和 OP18-093 的两张官方异画卡图。

## 技术说明

- 从简体中文官网卡表同步 121 条 OP18 卡图记录，更新 119 张基础 PNG，清理 4 张旧日文异画，并加入 OP18-093 的 2 张中文异画。
- 重新生成缩略图、高清 WebP 和卡图缓存版本，更新卡图清单及数据版本。

## 验证结果

- 官网记录 121 条，PNG 均为 454×635，下载无失败。
- `node scripts/check-card-image-assets.mjs` 通过。
- `node scripts/check-card-image-manifest.mjs public/data/imageManifest.json public` 通过。
- `node scripts/build-card-image-versions.mjs --check --physical` 通过。
