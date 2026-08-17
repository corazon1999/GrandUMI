# 卡牌效果热更新规则包

这套机制用于在不排空对局、不重启服务端的情况下修复单卡效果。每个房间创建时都会锁定一个不可变的 `rulesetId`：激活新版后，旧房间继续使用旧对象，新房间才读取新版；旧房间结束时，双方会收到“卡牌效果已更新，将从下一局开始生效”的提示。

## 规则包目录

运行时目录位于玩家 SQLite 数据库同级的 `Rulesets/<规则版本 ID>/`。管理页面会显示当前内置版本 ID、已发现的规则包，以及每个版本仍在进行的房间数。

一个只覆盖 DSL 卡效的规则包示例：

```text
Rulesets/
  hotfix-2026.08.17.1/
    manifest.json
    definitions/
      fixes.json
```

`manifest.json`：

```json
{
  "id": "hotfix-2026.08.17.1",
  "baseRulesetId": "builtin-当前构建提交号",
  "description": "修复 OP15-003 的登场效果",
  "definitionsDirectory": "definitions",
  "assemblies": [],
  "changedCards": ["OP15-003"]
}
```

`definitions/fixes.json` 使用与 `Effects/Definitions/*.json` 相同的结构，只需包含要覆盖的卡号。解析器会先复制基础规则，再按卡号整体替换定义。

## 手写 C# 卡效插件

DSL 无法表达时，可以编译一个引用当前 `GrandUMIServer.dll` 的类库，并在其中提供带无参构造函数的 `IScriptedEffect` 实现：

```csharp
using GrandUMI.Effects;

public sealed class Op15003Hotfix : IScriptedEffect
{
    public string CardNumber => "OP15-003";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        // 在这里实现修复后的效果。
        await Task.CompletedTask;
    }
}
```

把编译产物放进规则包并填写程序集相对路径：

```json
{
  "id": "hotfix-2026.08.17.2",
  "baseRulesetId": "hotfix-2026.08.17.1",
  "description": "修复 OP15-003 的复杂交互",
  "assemblies": ["plugins/CardEffectHotfix.dll"],
  "changedCards": ["OP15-003"]
}
```

插件程序集内同一卡号只能有一个实现；重复注册会拒绝加载。插件依赖应随 DLL 放在同目录，服务端核心程序集由加载器绑定到当前进程版本。

## 安全激活流程

1. 先在测试服使用相同基础版本运行对应单卡回归测试。
2. 将完整包上传到同卷的临时目录；所有文件写完后，再把目录重命名为最终规则版本 ID。不要直接向可见包目录逐个覆盖文件。
3. 管理员在主页“卡效热更新”面板点击“刷新规则包”。确认版本说明、变更卡号和基础版本正确。
4. 点击“激活给新对局”。激活指针会原子替换；正在进行的房间数量会按旧版本继续展示。
5. 观察错误日志和版本房间计数。旧局自然结束后玩家收到通知，之后创建的对局使用新规则。

每次修复必须使用新的规则版本 ID，已经加载的目录和文件不得原地修改或删除。服务端重启恢复对局时会按日志里的 `rulesetId` 重新加载原版本；缺失旧包时恢复会明确失败，不会静默套用新卡效。

需要回滚时，重新激活之前的规则版本即可。回滚同样只影响之后创建的对局，已经开始的新版对局仍使用其锁定版本。

## 能力边界

本机制适合修改 DSL 定义或单卡 `IScriptedEffect`。如果修复需要改变 `EffectRuntime`、通用原子操作、对局状态结构、网络协议或数据库结构，仍属于服务端核心发布，不能伪装成卡效插件热更；这类改动应按正常测试服验证和发布流程处理。
