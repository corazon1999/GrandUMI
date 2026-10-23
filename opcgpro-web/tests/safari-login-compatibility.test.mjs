import assert from "node:assert/strict";
import test from "node:test";
import { assertSafari16Syntax } from "../../tools/verify-login-browser.mjs";

test("构建检查拒绝导致 Safari 16.0 启动失败的类静态初始化块", () => {
  assert.throws(() => assertSafari16Syntax("class Boundary { static { this.contextType = context; } }"),
    /Safari 16.0 不支持/);
  assert.throws(() => assertSafari16Syntax("let Boundary = class extends Component { static { this.contextType = context; } };"),
    /Safari 16.0 不支持/);
});

test("构建检查允许兼容的类成员及包含类似文本的字符串", () => {
  assert.doesNotThrow(() => assertSafari16Syntax(
    'class Boundary { static contextType = context; static reset() {} } const text = "static {";',
  ));
});
