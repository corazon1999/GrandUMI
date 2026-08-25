# 补齐卡图发布清单完整性保护

- 日期：2026-08-22
- 分类：修复
- 影响范围：卡图资源同步、正式服预构建、卡牌异画显示
- 状态：已完成

## 玩家可见说明

- 强化卡图发布完整性检查，避免新增异画清单与实际图片文件不同步后在对局中显示黑色占位卡。

## 技术说明

- 新增无第三方依赖的图片清单校验器，将 `imageManifest.json` 中每个原图地址展开为缩略 WebP 与高清 WebP，并核对文件存在且非空。
- 正式服预构建在编译前校验共享卡图目录，存在任意清单引用缺图时立即停止。
- 香港卡图同步脚本在上传前校验本地资源、上传后按当前本地清单逐项核验正式服共享目录，防止静默漏传异画。

## 验证结果

- `node scripts/check-card-image-manifest.mjs public/data/imageManifest.json public` 通过，共核对 9,374 个清单派生文件。
- `npm run check:card-images` 通过，共审计 4,872 张原图及清单引用的 4,687 张卡图。
- `node --test tests/card-image-assets.test.mjs tests/new-production-deploy.test.mjs` 通过，共 16 项测试。
- `bash -n sync-assets-hk.sh ops/server/stage-grandumi-production.sh` 通过。
