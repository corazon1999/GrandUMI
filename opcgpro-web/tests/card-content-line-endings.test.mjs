import assert from "node:assert/strict";
import test from "node:test";
import { validateCanonicalLineEndings } from "../../tools/card-content-lib.mjs";

test("权威卡表使用LF时可保持Git检出前后的字节摘要", () => {
  assert.deepEqual(validateCanonicalLineEndings(Buffer.from('[\n  {"name":"娜美"}\n]\n'), "OP08.json"), []);
});

test("CRLF卡表必须在生成清单前明确拒绝", () => {
  assert.match(validateCanonicalLineEndings(Buffer.from('[\r\n]\r\n'), "OP08.json")[0], /OP08\.json.*必须使用 LF/);
});

test("孤立CR同样不能进入权威卡表摘要", () => {
  assert.equal(validateCanonicalLineEndings(Buffer.from('[\r]'), "P.json").length, 1);
});

test("JSON字符串中的转义回车不属于文件换行", () => {
  assert.deepEqual(validateCanonicalLineEndings(Buffer.from('[{"name":"\\r"}]\n'), "OP15.json"), []);
});
