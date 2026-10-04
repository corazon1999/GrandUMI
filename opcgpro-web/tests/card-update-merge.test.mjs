import assert from "node:assert/strict";
import test from "node:test";
import {
  canonicalSpritePath,
  describeCompatibility,
  hasValue,
  mergeImageSprites,
  mergeCardUpdates,
} from "../../tools/card-update-merge.mjs";

test("腾讯文档更新保留旧卡和新表空缺字段", () => {
  const existing = [
    { number: "OP18-001", name: "旧名称", color: "红", effectText: "旧效果" },
    { number: "OP18-002", name: "仅存在于旧数据", color: "蓝" },
  ];
  const imported = [
    { number: "OP18-001", name: "新名称", color: "绿", effectText: "" },
    { number: "OP18-003", name: "新增卡", color: "黄", effectText: "新效果" },
  ];

  const merged = mergeCardUpdates(existing, imported);
  assert.deepEqual(merged.map((card) => card.number), ["OP18-001", "OP18-002", "OP18-003"]);
  assert.deepEqual(merged[0], {
    number: "OP18-001",
    name: "新名称",
    color: "绿",
    effectText: "旧效果",
    alsoNames: ["旧名称"],
  });
  assert.equal(merged[1].name, "仅存在于旧数据");
});

test("兼容差异区分空值保留与非空冲突", () => {
  const report = describeCompatibility(
    [{ number: "OP18-001", name: "旧名称", color: "红", effectText: "旧效果" }],
    [{ number: "OP18-001", name: "新名称", color: "红", effectText: "" }],
  );
  assert.deepEqual(report.blankFieldsPreserved, [
    { number: "OP18-001", field: "effectText" },
  ]);
  assert.deepEqual(report.nonEmptyConflicts, [
    { number: "OP18-001", field: "name" },
  ]);
});

test("空数组和只含空白的字段不会覆盖旧值", () => {
  assert.equal(hasValue([]), false);
  assert.equal(hasValue(["", "  "]), false);
  assert.equal(hasValue(" \r\n "), false);
  assert.equal(hasValue(0), true);

  const merged = mergeCardUpdates(
    [{
      number: "OP18-001",
      name: "卡鲁",
      alsoNames: ["旧译名", "卡鲁"],
      effectTags: ["OnAnyCharKOd"],
      power: "5000",
    }],
    [{
      number: "OP18-001",
      name: "   ",
      alsoNames: [],
      effectTags: [],
      power: "   ",
    }],
  );

  assert.deepEqual(merged[0].alsoNames, ["旧译名"]);
  assert.equal(merged[0].name, "卡鲁");
  assert.deepEqual(merged[0].effectTags, ["OnAnyCharKOd"]);
  assert.equal(merged[0].power, "5000");
});

test("卡图清单按无查询参数路径排除正画并去重异画", () => {
  const existing = [
    "/cards/op18/OP18-001.png",
    "/cards/op18/OP18-001_01.png?v=old",
    "/cards/op18/OP18-001_02.png",
  ];
  const imported = [
    "/cards/op18/OP18-001.png?v=new",
    "/cards/op18/OP18-001_01.png?v=new-alt",
  ];

  assert.equal(
    canonicalSpritePath("/cards/op18/OP18-001.png?v=abc"),
    "/cards/op18/OP18-001.png",
  );
  assert.deepEqual(
    mergeImageSprites(existing, imported, "/cards/op18/OP18-001.png"),
    [
      "/cards/op18/OP18-001.png?v=new",
      "/cards/op18/OP18-001_01.png?v=new-alt",
      "/cards/op18/OP18-001_02.png",
    ],
  );
});
