import { DATA_VERSION } from "@/data/dataVersion";

// 浏览器统一使用 WebP 派生图；PNG/JPG 原图仅作为缺失派生图时的回退。
// - /cards-thumb/：宽 128，小卡、网格和列表使用。
// - /cards-webp/：最大宽 960，悬停、详情和全屏大图使用。
// - /sprites-thumb/：旧 sprites 资源已有的 400px WebP 派生图。
// 外部 URL、data URL 和已经是派生图的路径保持原样。

export const CARD_BACK_SRC = "/sprites-thumb/CardBack.webp";
const PRODUCTION_HOST = "grand-umi.com";
const PRODUCTION_DIRECT_ORIGIN = "https://grand-umi.com";
const CONFIGURED_ASSET_ORIGIN = (process.env.NEXT_PUBLIC_ASSET_ORIGIN ?? "").replace(/\/+$/, "");
// 正式服迁移期间源站曾短暂返回整批 404；追加修订号绕过 CDN 和浏览器中的负缓存。
const CARD_ASSET_VERSION = `${DATA_VERSION}-r4`;

const CARD_SOURCE_RE = /^\/cards\/(.+?)\.(png|jpe?g)([?#].*)?$/i;
const SPRITE_SOURCE_RE = /^\/sprites\/(.+?)\.(png|jpe?g)([?#].*)?$/i;

function mapLocalSource(
  src: string,
  cardDirectory: "cards-thumb" | "cards-webp",
): string {
  const cardMatch = src.match(CARD_SOURCE_RE);
  if (cardMatch) {
    return `/${cardDirectory}/${cardMatch[1]}.webp${cardMatch[3] ?? `?v=${CARD_ASSET_VERSION}`}`;
  }

  const spriteMatch = src.match(SPRITE_SOURCE_RE);
  if (spriteMatch) {
    return `/sprites-thumb/${spriteMatch[1]}.webp${spriteMatch[3] ?? `?v=${DATA_VERSION}`}`;
  }

  return src;
}

/** 配置静态资源域名后，将本地 public 资源统一交给该域名。 */
export function assetSrc(src: string): string {
  if (!CONFIGURED_ASSET_ORIGIN || !src.startsWith("/") || src.startsWith("//")) return src;
  return `${CONFIGURED_ASSET_ORIGIN}${src}`;
}

/** 小尺寸展示图：对战卡牌、网格、列表、领袖头像等。 */
export function thumbSrc(src?: string | null): string {
  return assetSrc(mapLocalSource(src || CARD_BACK_SRC, "cards-thumb"));
}

/** 大尺寸展示图：悬停预览、详情面板、Leader 开场和全屏大图。 */
export function displaySrc(src?: string | null): string {
  return assetSrc(mapLocalSource(src || CARD_BACK_SRC, "cards-webp"));
}

/**
 * Cloudflare 线路迟迟没有返回首字节时，允许图片切换到同一台源站的 IPv4 直连入口。
 * 只在正式主域启用，避免本地和测试环境意外跨环境读取资源。
 */
export function directAssetSrc(src: string): string | null {
  if (typeof window === "undefined" || window.location.hostname !== PRODUCTION_HOST) return null;

  const source = new URL(src, window.location.href);
  const configuredOrigin = CONFIGURED_ASSET_ORIGIN
    ? new URL(CONFIGURED_ASSET_ORIGIN).origin
    : window.location.origin;
  if (source.origin !== window.location.origin && source.origin !== configuredOrigin) return null;
  return new URL(`${source.pathname}${source.search}${source.hash}`, PRODUCTION_DIRECT_ORIGIN).href;
}

function imageFallbackSources(candidates: Array<string | null | undefined>): string[] {
  const sources: string[] = [];
  for (const source of candidates) {
    if (!source || sources.includes(source)) continue;
    sources.push(source);
    const directSource = directAssetSrc(source);
    if (directSource && !sources.includes(directSource)) sources.push(directSource);
  }
  return sources;
}

function absoluteImageSrc(src: string): string {
  return typeof window === "undefined" ? src : new URL(src, window.location.href).href;
}

/** React 图片状态在加载失败时取得下一个候选，确保不会在原图和外部图之间循环。 */
export function nextCardImageSrc(
  currentSrc: string,
  rawSrc: string,
  externalSrc: string | null | undefined,
  variant: "thumb" | "display",
): string {
  const derivedSrc = variant === "thumb" ? thumbSrc(rawSrc) : displaySrc(rawSrc);
  const sources = imageFallbackSources([
    derivedSrc,
    derivedSrc !== rawSrc ? rawSrc : null,
    externalSrc,
    CARD_BACK_SRC,
  ]);
  const currentIndex = sources.findIndex((source) => absoluteImageSrc(source) === absoluteImageSrc(currentSrc));
  return currentIndex >= 0 ? (sources[currentIndex + 1] ?? currentSrc) : (sources[0] ?? currentSrc);
}

/** 判断路径是否为缩略图(用于加载失败时回退原图) */
export function isThumbSrc(src: string): boolean {
  return src.startsWith("/cards-thumb/") || src.startsWith("/sprites-thumb/");
}

/** 判断路径是否为本项目生成的 WebP 派生资源。 */
export function isDerivedImageSrc(src: string): boolean {
  return isThumbSrc(src) || src.startsWith("/cards-webp/");
}

/**
 * 给原生 img/Next Image 的 onError 使用，按顺序尝试原图、外部图和卡背。
 * 组件无需为很少发生的派生图缺失单独维护 React 状态。
 */
export function advanceImageFallback(
  image: HTMLImageElement,
  candidates: Array<string | null | undefined>,
): void {
  const sources = imageFallbackSources([...candidates, CARD_BACK_SRC]);
  const signature = sources.join("\n");
  if (image.dataset.fallbackSignature !== signature) {
    image.dataset.fallbackSignature = signature;
    image.dataset.fallbackIndex = "0";
  }

  let index = Number(image.dataset.fallbackIndex ?? "0");
  while (index < sources.length) {
    const next = sources[index++];
    image.dataset.fallbackIndex = String(index);
    const absoluteNext = new URL(next, window.location.href).href;
    if (absoluteNext !== image.src) {
      image.src = next;
      return;
    }
  }
}
