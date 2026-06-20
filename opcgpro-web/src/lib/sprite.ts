// 把原图 sprite 路径映射到缩略图路径,用于卡组编辑器网格与悬停预览。
// - /cards/ 下的 .png → /cards-thumb/ 的 .webp(由 gen-card-thumbs.mjs 生成,宽256 webp,约为原图 15%)
// - /sprites/ 下的 .png → /sprites-thumb/ 的 .webp(旧 gen-thumbs.mjs 生成)
// 其余路径(如外部 URL)原样返回。
export function thumbSrc(src?: string | null): string {
  if (!src) return "/sprites/CardBack.png";
  if (src.startsWith("/cards/") && /\.png$/i.test(src)) {
    return "/cards-thumb/" + src.slice("/cards/".length).replace(/\.png$/i, ".webp");
  }
  if (src.startsWith("/sprites/") && /\.png$/i.test(src)) {
    return "/sprites-thumb/" + src.slice("/sprites/".length).replace(/\.png$/i, ".webp");
  }
  return src;
}

/** 判断路径是否为缩略图(用于加载失败时回退原图) */
export function isThumbSrc(src: string): boolean {
  return src.startsWith("/cards-thumb/") || src.startsWith("/sprites-thumb/");
}
