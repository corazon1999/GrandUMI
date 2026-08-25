import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import ts from "typescript";

const here = path.dirname(fileURLToPath(import.meta.url));
const projectRoot = path.resolve(here, "..");

async function readSource(relativePath) {
  return fs.readFile(path.join(projectRoot, relativePath), "utf8");
}

function parseTsx(source, fileName) {
  return ts.createSourceFile(fileName, source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
}

function findElementByDataAttribute(sourceFile, attributeName) {
  let match = null;
  const visit = (node) => {
    if (ts.isJsxOpeningElement(node) || ts.isJsxSelfClosingElement(node)) {
      const hasAttribute = node.attributes.properties.some(
        (attribute) => ts.isJsxAttribute(attribute) && attribute.name.text === attributeName,
      );
      if (hasAttribute) match = node;
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);
  return match;
}

function getAttribute(element, attributeName) {
  return element.attributes.properties.find(
    (attribute) => ts.isJsxAttribute(attribute) && attribute.name.text === attributeName,
  );
}

function assertClientHomeLink(source, fileName, dataAttribute) {
  const sourceFile = parseTsx(source, fileName);
  const linkImport = sourceFile.statements.find(
    (statement) => ts.isImportDeclaration(statement)
      && statement.moduleSpecifier.text === "next/link"
      && statement.importClause?.name?.text === "Link",
  );
  assert.ok(linkImport, `${fileName} 应导入 Next Link`);

  const element = findElementByDataAttribute(sourceFile, dataAttribute);
  assert.ok(element, `${fileName} 应保留 ${dataAttribute} 入口`);
  assert.equal(element.tagName.getText(sourceFile), "Link", "返回大厅必须走客户端路由");
  assert.equal(getAttribute(element, "href")?.initializer?.text, "/home");
  assert.equal(getAttribute(element, "aria-label")?.initializer?.text, "返回大厅");
  assert.equal(getAttribute(element, "onClick"), undefined, "客户端返回不应触发整页恢复标记");
}

test("桌面返回大厅保持根布局会话，不重建登录首屏", async () => {
  const source = await readSource("src/components/deck-editor/DeckInfoPanel.tsx");

  assertClientHomeLink(source, "DeckInfoPanel.tsx", "data-deck-editor-back-link");
});

test("手机竖屏主导航共用客户端会话返回，且触控区域不小于 44px", async () => {
  const source = await readSource("src/app/deck-editor/page.tsx");

  assertClientHomeLink(source, "page.tsx", "data-deck-mobile-back");
  assert.match(source, /grid-cols-\[auto_auto_1fr_1fr\]/);
  assert.match(source, /min-h-11 min-w-11/);
});
