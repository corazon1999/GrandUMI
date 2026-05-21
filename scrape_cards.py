"""
航海王卡牌对战 — 卡图批量下载脚本
目标网站: https://www.onepiece-cardgame.cn/
运行方式:
    pip install playwright aiohttp aiofiles
    playwright install chromium
    python scrape_cards.py
"""

import asyncio
import re
import json
import os
from pathlib import Path
from urllib.parse import urljoin, urlparse
from collections import defaultdict

import aiohttp
import aiofiles
from playwright.async_api import async_playwright, Page, Request

# ──────────────────────────────────────────────
# 配置
# ──────────────────────────────────────────────
BASE_URL    = "https://www.onepiece-cardgame.cn"
OUTPUT_DIR  = Path(__file__).parent / "CardImages"
CONCURRENCY = 5          # 同时下载的并发数，避免被封
HEADLESS    = True       # 调试时改 False 可看到浏览器

# 卡号正则：匹配 OP01-001、ST01-001、EB01-001、P-001 等格式
CARD_NO_RE  = re.compile(r'\b([A-Z]{1,4}\d{0,2}-\d{3,})\b', re.IGNORECASE)

# 已知的卡集前缀，用于目录名兜底
KNOWN_SETS  = [
    "ST01","ST02","ST03","ST04","ST05","ST06","ST07","ST08","ST09","ST10",
    "ST11","ST12","ST13","ST14","ST15","ST16","ST17","ST18","ST19","ST20",
    "ST21","ST22","ST23","ST24","ST25","ST26","ST27","ST28","ST29",
    "OP01","OP02","OP03","OP04","OP05","OP06","OP07","OP08","OP09","OP10",
    "OP11","OP12","OP13","OP14","OP15",
    "EB01","EB02","EB03","EB04",
    "P",
]

# ──────────────────────────────────────────────
# 工具函数
# ──────────────────────────────────────────────

def get_set_id(card_no: str) -> str:
    """从卡号提取卡集 ID，如 OP01-001 → OP01"""
    m = re.match(r'^([A-Z]{1,4}\d{0,2})-', card_no, re.IGNORECASE)
    return m.group(1).upper() if m else "UNKNOWN"


def extract_set_from_url(url: str) -> str | None:
    """从图片 URL 猜测卡集 ID"""
    for s in KNOWN_SETS:
        if s.lower() in url.lower() or s in url:
            return s
    return None


def build_save_path(card_no: str, version: int, ext: str = "png") -> Path:
    """构建保存路径：CardImages/<SetID>/<card_no>_<version>.<ext>"""
    set_id = get_set_id(card_no)
    dir_   = OUTPUT_DIR / set_id
    dir_.mkdir(parents=True, exist_ok=True)
    return dir_ / f"{card_no}_{version}.{ext}"


# ──────────────────────────────────────────────
# 阶段 1：用 Playwright 拦截网络请求，收集卡图 URL
# ──────────────────────────────────────────────

class CardImageCollector:
    def __init__(self):
        self.image_urls: dict[str, list[str]] = defaultdict(list)  # card_no → [url, ...]
        self.unknown_images: list[str] = []   # 无法解析卡号的图片 URL
        self.api_responses: list[dict] = []   # 捕获的 JSON API 响应

    def on_request(self, request: Request):
        url = request.url
        # 过滤出看起来像卡牌图片的请求
        if self._is_card_image(url):
            card_no = self._parse_card_no(url)
            if card_no:
                if url not in self.image_urls[card_no]:
                    self.image_urls[card_no].append(url)
                    print(f"  [图片] {card_no} → {url}")
            else:
                if url not in self.unknown_images:
                    self.unknown_images.append(url)

    async def on_response(self, response):
        url = response.url
        ct  = response.headers.get("content-type", "")
        if "json" in ct and any(k in url for k in ["/api/", "/card", "/list"]):
            try:
                data = await response.json()
                self.api_responses.append({"url": url, "data": data})
                print(f"  [API ] {url}")
                # 尝试从 JSON 里提取图片 URL
                self._parse_api_response(data)
            except Exception:
                pass

    def _is_card_image(self, url: str) -> bool:
        url_lower = url.lower()
        # 排除 UI 图、图标等
        if any(x in url_lower for x in ["icon", "logo", "bg", "banner", "ui", "font", "css", ".js"]):
            return False
        # 包含图片扩展名且路径含 card/image 关键字
        if re.search(r'\.(png|jpg|webp|jpeg)', url_lower):
            if any(k in url_lower for k in ["card", "img", "image", "cards"]):
                return True
        return False

    def _parse_card_no(self, url: str) -> str | None:
        m = CARD_NO_RE.search(url)
        return m.group(1).upper() if m else None

    def _parse_api_response(self, data, depth=0):
        """递归从 JSON 数据中提取图片 URL"""
        if depth > 5:
            return
        if isinstance(data, dict):
            for v in data.values():
                self._parse_api_response(v, depth + 1)
            # 常见字段名
            for key in ("img", "image", "imageUrl", "img_url", "card_image", "picture", "src", "url"):
                if key in data and isinstance(data[key], str):
                    url = data[key]
                    if not url.startswith("http"):
                        url = urljoin(BASE_URL, url)
                    card_no = self._parse_card_no(url)
                    if card_no and url not in self.image_urls[card_no]:
                        self.image_urls[card_no].append(url)
        elif isinstance(data, list):
            for item in data:
                self._parse_api_response(item, depth + 1)


async def collect_card_urls(collector: CardImageCollector):
    """启动浏览器，浏览所有卡集页面，收集图片 URL"""
    async with async_playwright() as pw:
        browser = await pw.chromium.launch(headless=HEADLESS)
        ctx     = await browser.new_context(
            user_agent="Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
                       "AppleWebKit/537.36 (KHTML, like Gecko) "
                       "Chrome/124.0.0.0 Safari/537.36"
        )
        page = await ctx.new_page()

        # 绑定拦截器
        page.on("request",  collector.on_request)
        page.on("response", collector.on_response)

        # ── 1. 打开首页，等待完全加载
        print("\n[1/4] 打开首页...")
        await page.goto(BASE_URL, wait_until="networkidle", timeout=30_000)
        await page.wait_for_timeout(2_000)

        # ── 2. 找卡牌列表入口（常见选择器，按优先级尝试）
        print("[2/4] 寻找卡牌列表页面...")
        nav_selectors = [
            'a[href*="cardlist"]',
            'a[href*="card_list"]',
            'a[href*="cards"]',
            'nav a',
            '.menu a',
            '.nav a',
        ]
        nav_link = None
        for sel in nav_selectors:
            try:
                el = page.locator(sel).first
                if await el.count() > 0:
                    nav_link = el
                    break
            except Exception:
                pass

        if nav_link:
            await nav_link.click()
            await page.wait_for_load_state("networkidle")
            await page.wait_for_timeout(2_000)
        else:
            # 直接尝试常见路径
            for path in ["/cardlist", "/card_list", "/cards", "/cardlist/"]:
                try:
                    await page.goto(BASE_URL + path, wait_until="networkidle", timeout=15_000)
                    await page.wait_for_timeout(2_000)
                    break
                except Exception:
                    continue

        # ── 3. 找所有卡集/系列选项并逐一点击
        print("[3/4] 遍历所有卡集...")
        series_selectors = [
            'select[name*="series"] option',
            'select[id*="series"] option',
            '.series-list li',
            '.card-series li',
            '.expansion-list li',
            '[class*="series"] li',
            '[class*="pack"] li',
            '.filter-group a',
        ]

        series_items = []
        for sel in series_selectors:
            items = page.locator(sel)
            cnt   = await items.count()
            if cnt > 1:
                series_items = [await items.nth(i).get_attribute("value") or
                                await items.nth(i).inner_text()
                                for i in range(cnt)]
                print(f"  找到 {cnt} 个卡集（选择器: {sel}）")
                break

        if series_items:
            for s in series_items:
                print(f"  → 加载卡集: {s}")
                # 如果是 <select>，设置 value 并触发 change
                try:
                    sel_el = page.locator('select[name*="series"], select[id*="series"]').first
                    await sel_el.select_option(value=s)
                    await page.wait_for_timeout(2_000)
                    await _scroll_to_bottom(page)
                except Exception:
                    pass
        else:
            # 没找到系列导航，直接滚动当前页面加载全部
            print("  未找到系列导航，滚动当前页面加载所有卡牌...")
            await _scroll_to_bottom(page)

        # ── 4. 最后滚动一遍，触发懒加载
        print("[4/4] 触发懒加载...")
        await _scroll_to_bottom(page)
        await page.wait_for_timeout(3_000)

        # 保存 API 响应到文件（调试用）
        if collector.api_responses:
            debug_file = OUTPUT_DIR / "_api_responses.json"
            debug_file.parent.mkdir(parents=True, exist_ok=True)
            async with aiofiles.open(debug_file, "w", encoding="utf-8") as f:
                await f.write(json.dumps(collector.api_responses, ensure_ascii=False, indent=2))
            print(f"  API 响应已保存至: {debug_file}")

        await browser.close()


async def _scroll_to_bottom(page: Page):
    """缓慢滚动到底部，触发懒加载图片"""
    prev_height = 0
    for _ in range(20):
        height = await page.evaluate("document.body.scrollHeight")
        if height == prev_height:
            break
        prev_height = height
        await page.evaluate("window.scrollTo(0, document.body.scrollHeight)")
        await page.wait_for_timeout(800)


# ──────────────────────────────────────────────
# 阶段 2：下载图片
# ──────────────────────────────────────────────

async def download_all(collector: CardImageCollector):
    total      = sum(len(v) for v in collector.image_urls.values())
    unknown_n  = len(collector.unknown_images)
    print(f"\n共找到 {len(collector.image_urls)} 张卡（{total} 个图片 URL）"
          f"，{unknown_n} 个无法解析卡号的 URL")

    if total == 0:
        print("\n⚠ 未捕获到任何卡牌图片 URL。")
        print("   可能原因：")
        print("   1. 网站图片使用 Canvas/CSS 背景加载，而非 <img> 标签")
        print("   2. 图片 URL 格式不包含卡号（如纯哈希路径）")
        print("   建议：将 HEADLESS = False 重新运行，手动点击几张卡，")
        print("         观察 _api_responses.json 中的数据结构，再调整脚本。")
        return

    sem = asyncio.Semaphore(CONCURRENCY)
    timeout = aiohttp.ClientTimeout(total=30)
    headers = {"Referer": BASE_URL,
               "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
                             "AppleWebKit/537.36 Chrome/124.0 Safari/537.36"}

    async with aiohttp.ClientSession(timeout=timeout, headers=headers) as session:
        tasks = []
        # 已知卡号的图片
        for card_no, urls in collector.image_urls.items():
            for ver_idx, url in enumerate(urls, start=1):
                ext = Path(urlparse(url).path).suffix.lstrip(".") or "png"
                save_path = build_save_path(card_no, ver_idx, ext)
                tasks.append(_download_one(session, sem, url, save_path))

        # 无法解析卡号的图片，放入 UNKNOWN 目录
        unk_dir = OUTPUT_DIR / "UNKNOWN"
        unk_dir.mkdir(parents=True, exist_ok=True)
        for idx, url in enumerate(collector.unknown_images, start=1):
            ext  = Path(urlparse(url).path).suffix.lstrip(".") or "png"
            name = f"unknown_{idx:04d}.{ext}"
            save_path = unk_dir / name
            tasks.append(_download_one(session, sem, url, save_path))

        results = await asyncio.gather(*tasks, return_exceptions=True)

    ok  = sum(1 for r in results if r is True)
    err = sum(1 for r in results if r is not True)
    print(f"\n✅ 下载完成：{ok} 成功，{err} 失败")
    print(f"📁 保存目录：{OUTPUT_DIR.resolve()}")


async def _download_one(session: aiohttp.ClientSession, sem: asyncio.Semaphore,
                        url: str, save_path: Path) -> bool:
    # 已存在则跳过（断点续传）
    if save_path.exists() and save_path.stat().st_size > 1024:
        return True

    async with sem:
        try:
            async with session.get(url) as resp:
                if resp.status != 200:
                    print(f"  [跳过] HTTP {resp.status} → {url}")
                    return False
                data = await resp.read()
            async with aiofiles.open(save_path, "wb") as f:
                await f.write(data)
            print(f"  [OK] {save_path.relative_to(OUTPUT_DIR)}")
            return True
        except Exception as e:
            print(f"  [ERR] {save_path.name}: {e}")
            return False


# ──────────────────────────────────────────────
# 入口
# ──────────────────────────────────────────────

async def main():
    print("=" * 60)
    print("  航海王卡牌对战 — 卡图批量下载")
    print(f"  输出目录: {OUTPUT_DIR.resolve()}")
    print("=" * 60)

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

    collector = CardImageCollector()

    print("\n── 阶段 1：浏览器爬取（拦截网络请求）──")
    await collect_card_urls(collector)

    print("\n── 阶段 2：下载图片 ──")
    await download_all(collector)


if __name__ == "__main__":
    asyncio.run(main())
