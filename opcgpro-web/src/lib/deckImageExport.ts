import type { DeckEntry } from "@/store/deckStore";
import type { CardData } from "@/types/card";
import { CARD_BACK_SRC, displaySrc } from "@/lib/sprite";

export const DECK_IMAGE_WIDTH = 1440;
export const DECK_IMAGE_COLUMNS = 5;

const OUTER_PADDING = 48;
const HEADER_HEIGHT = 152;
const SECTION_BAR_HEIGHT = 54;
const SECTION_TOP_GAP = 20;
const SECTION_GAP = 42;
const CARD_GAP_X = 18;
const CARD_GAP_Y = 32;
const CARD_WIDTH = Math.floor(
  (DECK_IMAGE_WIDTH - OUTER_PADDING * 2 - CARD_GAP_X * (DECK_IMAGE_COLUMNS - 1)) /
    DECK_IMAGE_COLUMNS,
);
const CARD_HEIGHT = Math.round(CARD_WIDTH * 1.4);
const CARD_LABEL_HEIGHT = 42;
const CANVAS_BOTTOM_PADDING = 48;

const FONT_FAMILY = '"Microsoft YaHei", "Noto Sans SC", Arial, sans-serif';

interface DeckImageOptions {
  deckName: string;
  leader: CardData;
  entries: DeckEntry[];
}

interface SectionLayout {
  barY: number;
  cardsY: number;
  rows: number;
  bottom: number;
}

function sectionHeight(rows: number): number {
  return (
    SECTION_BAR_HEIGHT +
    SECTION_TOP_GAP +
    rows * (CARD_HEIGHT + CARD_LABEL_HEIGHT) +
    Math.max(0, rows - 1) * CARD_GAP_Y
  );
}

function buildLayout(entryCount: number): {
  main: SectionLayout;
  leader: SectionLayout;
  height: number;
} {
  const mainRows = Math.max(1, Math.ceil(entryCount / DECK_IMAGE_COLUMNS));
  const mainBarY = HEADER_HEIGHT;
  const mainCardsY = mainBarY + SECTION_BAR_HEIGHT + SECTION_TOP_GAP;
  const mainBottom = mainBarY + sectionHeight(mainRows);
  const leaderBarY = mainBottom + SECTION_GAP;
  const leaderCardsY = leaderBarY + SECTION_BAR_HEIGHT + SECTION_TOP_GAP;
  const leaderBottom = leaderBarY + sectionHeight(1);

  return {
    main: { barY: mainBarY, cardsY: mainCardsY, rows: mainRows, bottom: mainBottom },
    leader: { barY: leaderBarY, cardsY: leaderCardsY, rows: 1, bottom: leaderBottom },
    height: leaderBottom + CANVAS_BOTTOM_PADDING,
  };
}

function roundedRect(
  context: CanvasRenderingContext2D,
  x: number,
  y: number,
  width: number,
  height: number,
  radius: number,
): void {
  const r = Math.min(radius, width / 2, height / 2);
  context.beginPath();
  context.moveTo(x + r, y);
  context.arcTo(x + width, y, x + width, y + height, r);
  context.arcTo(x + width, y + height, x, y + height, r);
  context.arcTo(x, y + height, x, y, r);
  context.arcTo(x, y, x + width, y, r);
  context.closePath();
}

function drawContainedImage(
  context: CanvasRenderingContext2D,
  image: HTMLImageElement,
  x: number,
  y: number,
): void {
  context.save();
  roundedRect(context, x, y, CARD_WIDTH, CARD_HEIGHT, 12);
  context.clip();
  context.fillStyle = "#050507";
  context.fillRect(x, y, CARD_WIDTH, CARD_HEIGHT);

  const scale = Math.min(CARD_WIDTH / image.naturalWidth, CARD_HEIGHT / image.naturalHeight);
  const width = image.naturalWidth * scale;
  const height = image.naturalHeight * scale;
  context.drawImage(
    image,
    x + (CARD_WIDTH - width) / 2,
    y + (CARD_HEIGHT - height) / 2,
    width,
    height,
  );
  context.restore();

  context.strokeStyle = "rgba(255,255,255,0.16)";
  context.lineWidth = 2;
  roundedRect(context, x, y, CARD_WIDTH, CARD_HEIGHT, 12);
  context.stroke();
}

function drawQuantityBadge(
  context: CanvasRenderingContext2D,
  x: number,
  y: number,
  count: number,
): void {
  const centerX = x + CARD_WIDTH - 25;
  const centerY = y + 25;
  context.save();
  context.shadowColor = "rgba(0,0,0,0.65)";
  context.shadowBlur = 8;
  context.beginPath();
  context.arc(centerX, centerY, 23, 0, Math.PI * 2);
  context.fillStyle = "#0b0a10";
  context.fill();
  context.shadowBlur = 0;
  context.lineWidth = 2;
  context.strokeStyle = "rgba(255,255,255,0.82)";
  context.stroke();
  context.fillStyle = "#ffffff";
  context.font = `700 25px ${FONT_FAMILY}`;
  context.textAlign = "center";
  context.textBaseline = "middle";
  context.fillText(String(count), centerX, centerY + 1);
  context.restore();
}

function drawCardNumber(
  context: CanvasRenderingContext2D,
  card: CardData,
  x: number,
  y: number,
): void {
  context.fillStyle = "#e5e7eb";
  context.font = `600 24px ${FONT_FAMILY}`;
  context.textAlign = "center";
  context.textBaseline = "middle";
  context.fillText(card.number, x + CARD_WIDTH / 2, y + CARD_HEIGHT + CARD_LABEL_HEIGHT / 2);
}

function drawSectionBar(
  context: CanvasRenderingContext2D,
  y: number,
  englishLabel: string,
  chineseLabel: string,
  count: number,
): void {
  const gradient = context.createLinearGradient(OUTER_PADDING, y, DECK_IMAGE_WIDTH - OUTER_PADDING, y);
  gradient.addColorStop(0, "#17151f");
  gradient.addColorStop(1, "#0e0d14");
  context.fillStyle = gradient;
  roundedRect(context, OUTER_PADDING, y, DECK_IMAGE_WIDTH - OUTER_PADDING * 2, SECTION_BAR_HEIGHT, 12);
  context.fill();
  context.fillStyle = "#f97316";
  roundedRect(context, OUTER_PADDING, y, 8, SECTION_BAR_HEIGHT, 4);
  context.fill();
  context.fillStyle = "#ffffff";
  context.font = `700 25px ${FONT_FAMILY}`;
  context.textAlign = "left";
  context.textBaseline = "middle";
  context.fillText(`${englishLabel} · ${chineseLabel} ${count}`, OUTER_PADDING + 26, y + SECTION_BAR_HEIGHT / 2);
}

function imageCandidates(card: CardData): string[] {
  const rawSprite = card.sprite ?? CARD_BACK_SRC;
  return [...new Set([displaySrc(rawSprite), rawSprite, card.image, CARD_BACK_SRC].filter((source): source is string => !!source))];
}

function loadImageCandidate(source: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const image = new Image();
    const absoluteUrl = new URL(source, window.location.href);
    if (absoluteUrl.origin !== window.location.origin) image.crossOrigin = "anonymous";
    image.decoding = "async";

    const timer = window.setTimeout(() => {
      image.src = "";
      reject(new Error(`卡图加载超时: ${source}`));
    }, 12_000);

    image.onload = () => {
      window.clearTimeout(timer);
      resolve(image);
    };
    image.onerror = () => {
      window.clearTimeout(timer);
      reject(new Error(`卡图加载失败: ${source}`));
    };
    image.src = absoluteUrl.href;
  });
}

async function loadCardImage(card: CardData): Promise<HTMLImageElement> {
  let lastError: unknown;
  for (const source of imageCandidates(card)) {
    try {
      return await loadImageCandidate(source);
    } catch (error) {
      lastError = error;
    }
  }
  throw lastError instanceof Error ? lastError : new Error(`无法加载卡图: ${card.number}`);
}

function drawCard(
  context: CanvasRenderingContext2D,
  image: HTMLImageElement,
  card: CardData,
  count: number,
  index: number,
  cardsY: number,
): void {
  const column = index % DECK_IMAGE_COLUMNS;
  const row = Math.floor(index / DECK_IMAGE_COLUMNS);
  const x = OUTER_PADDING + column * (CARD_WIDTH + CARD_GAP_X);
  const y = cardsY + row * (CARD_HEIGHT + CARD_LABEL_HEIGHT + CARD_GAP_Y);
  drawContainedImage(context, image, x, y);
  drawQuantityBadge(context, x, y, count);
  drawCardNumber(context, card, x, y);
}

function canvasToBlob(canvas: HTMLCanvasElement): Promise<Blob> {
  return new Promise((resolve, reject) => {
    canvas.toBlob((blob) => {
      if (blob) resolve(blob);
      else reject(new Error("浏览器未能生成 PNG 图片"));
    }, "image/png");
  });
}

function safeFilename(deckName: string): string {
  const sanitized = deckName.trim().replace(/[<>:"/\\|?*\u0000-\u001f]/g, "-");
  return `${sanitized || "未命名卡组"}-一图流.png`;
}

export async function renderDeckImage({
  deckName,
  leader,
  entries,
}: DeckImageOptions): Promise<HTMLCanvasElement> {
  const layout = buildLayout(entries.length);
  const canvas = document.createElement("canvas");
  canvas.width = DECK_IMAGE_WIDTH;
  canvas.height = layout.height;
  const context = canvas.getContext("2d");
  if (!context) throw new Error("当前浏览器不支持图片导出");

  const background = context.createLinearGradient(0, 0, 0, canvas.height);
  background.addColorStop(0, "#111019");
  background.addColorStop(1, "#08070c");
  context.fillStyle = background;
  context.fillRect(0, 0, canvas.width, canvas.height);

  context.fillStyle = "#f97316";
  context.fillRect(0, 0, canvas.width, 8);
  context.fillStyle = "#ffffff";
  context.font = `700 48px ${FONT_FAMILY}`;
  context.textAlign = "left";
  context.textBaseline = "alphabetic";
  context.fillText(deckName.trim() || "未命名卡组", OUTER_PADDING, 76);
  context.fillStyle = "#9ca3af";
  context.font = `500 23px ${FONT_FAMILY}`;
  const mainCount = entries.reduce((sum, entry) => sum + entry.count, 0);
  context.fillText(`主卡组 ${mainCount} 张 · ${entries.length} 种卡牌 · 领袖 ${leader.name}`, OUTER_PADDING, 116);
  context.fillStyle = "#6b7280";
  context.font = `600 20px ${FONT_FAMILY}`;
  context.textAlign = "right";
  context.fillText("GrandUMI · DECK IMAGE", DECK_IMAGE_WIDTH - OUTER_PADDING, 72);

  drawSectionBar(context, layout.main.barY, "MAIN DECK", "主卡组", mainCount);
  drawSectionBar(context, layout.leader.barY, "LEADER", "领袖", 1);

  const [mainImages, leaderImage] = await Promise.all([
    Promise.all(entries.map((entry) => loadCardImage(entry.card))),
    loadCardImage(leader),
  ]);
  entries.forEach((entry, index) => {
    drawCard(context, mainImages[index], entry.card, entry.count, index, layout.main.cardsY);
  });
  drawCard(context, leaderImage, leader, 1, 0, layout.leader.cardsY);

  return canvas;
}

export async function downloadDeckImage(options: DeckImageOptions): Promise<void> {
  const canvas = await renderDeckImage(options);
  const blob = await canvasToBlob(canvas);
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = safeFilename(options.deckName);
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 1_000);
}
