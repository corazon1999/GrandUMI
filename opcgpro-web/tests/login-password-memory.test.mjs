import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const readSource = (path) => readFile(new URL(path, import.meta.url), "utf8");

test("登录成功后按账号记住密码并在刷新流程恢复", async () => {
  const source = await readSource("../src/components/home/LoginPanel.tsx");

  assert.match(source, /PASSWORD_STORAGE_PREFIX = "grandumi_password:"/);
  assert.match(source, /if \(submitted\?\.password\) rememberPassword\(submitted\.account, submitted\.password\)/);
  assert.match(source, /setPassword\(nextStep === "setup" \? "" : loadRememberedPassword\(nextAccount\)\)/);
  assert.match(source, /passwordStorageKey\(normalized\)/);
});

test("密码框提供可访问的显示隐藏按钮", async () => {
  const source = await readSource("../src/components/home/LoginPanel.tsx");

  assert.match(source, /type=\{showPassword \? "text" : "password"\}/);
  assert.match(source, /aria-label=\{showPassword \? "隐藏密码" : "显示密码"\}/);
  assert.match(source, /aria-pressed=\{showPassword\}/);
  assert.match(source, /onClick=\{\(\) => setShowPassword\(\(visible\) => !visible\)\}/);
});
