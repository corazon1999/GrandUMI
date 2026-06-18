# GrandUMI 卡效全量补全 — 自动运行状态

(用户外出，授权全自主推进；明天验收。此文件随进度更新。)

## 总目标
1. OP09–16：DSL(已完成 153) + 为 complex 卡写 C# 脚本。
2. OP05–08：从零补全 DSL + C# 脚本。

## 已完成 ✅
- 引擎小修：Choose/AddPowerAll/SearchDeck 的 filter 改用 BuildMatchPredicate（anyOf/excludeName 生效），编译通过。
- OP16_gap.json JSON 损坏修复。
- OP09–16 DSL：153 张写入各 `Definitions/OPxx_wf.json`，服务端验证加载 0 报错。
- C# pilot：12 张 → 10 脚本化(全量重编译 0 错误) + 2 complex。C# 流水线已验证可行。

## 进度更新
- ✅ OP09–16 C# 生成完成：104+10(pilot)=**114 脚本化**，全量重编译仅 1 处错误(OP16_012 用了 c.Info.MatchesName，已修)，现 **0 错误**。96 张 complex。
- ✅ OP05–08 DSL 完成：**189 张** 合并入 OP05/06/07/08_wf.json（52/43/55/39），全部 JSON 解析通过。217+2 complex。

## 进行中 🔄
- 工作流 C：OP05–08 complex（219 张）C# 脚本生成（37 批）→ 完成后编译验证。

## 待办 ⏭
1. C 完成 → 全量重编译；若有错，逐个修复直到 0 错误。
2. 全部完成 → 启动服务端验证 DSL 加载 0 报错 + 重启常驻窗口。
3. 写最终验收报告 `_FINAL_REPORT.md`。
4. 清理临时文件（保留报告与 spec）。

## 关键脚本/产物
- DSL spec: _dsl_spec.md ; C# spec: _cs_spec.md
- DSL 工作流: _wf_effects.js ; C# 工作流: _wf_cs_gen.js
- 校验合并: _merge_validate.js + _do_merge.js
- complex 清单: _complex_report.json ; 复杂卡数据: _complex_items.json

## 质量声明（给验收）
- 所有效果为机器翻译/生成 + 静态校验(白名单/编译)，未逐张实战测试，建议重点卡上桌验证。
- 引擎确实无通道的机制(非力量持续修正、改攻击目标、复制、流放、缺事件钩子)统一判 complex 并列清单。
