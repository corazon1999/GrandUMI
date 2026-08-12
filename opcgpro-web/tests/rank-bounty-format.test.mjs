import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import ts from "typescript";

const source = await readFile(new URL("../src/lib/rankBounty.ts", import.meta.url), "utf8");
const javascript = ts.transpile(source, { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 });
const moduleUrl = `data:text/javascript;base64,${Buffer.from(javascript).toString("base64")}`;
const { formatRankBounty, formatSignedRankBounty } = await import(moduleUrl);

test("排位分按每分十万换算为贝里", () => {
  assert.equal(formatRankBounty(0), "0贝里");
  assert.equal(formatRankBounty(1), "10万贝里");
  assert.equal(formatRankBounty(20), "200万贝里");
  assert.equal(formatRankBounty(1000), "1亿贝里");
  assert.equal(formatRankBounty(1286), "1亿2860万贝里");
  assert.equal(formatRankBounty(1500), "1亿5000万贝里");
});

test("悬赏金变化保留正负号", () => {
  assert.equal(formatSignedRankBounty(20), "+200万贝里");
  assert.equal(formatSignedRankBounty(-20), "-200万贝里");
  assert.equal(formatSignedRankBounty(0), "0贝里");
});
