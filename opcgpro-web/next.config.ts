import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // 页面运行所必需的 Next.js JS/CSS 始终使用当前站点同源地址，避免静态资源域
  // 被拦截、解析失败或边缘缓存版本不一致时只剩空白页面。卡图等大体积 public
  // 资源仍由 sprite.ts 根据 NEXT_PUBLIC_ASSET_ORIGIN 路由到独立静态域名。
  images: {
    // 卡牌是本地静态游戏图，无需优化；关闭优化避免 dev 的 /_next/image
    // 端点在大量卡图并发时拖垮服务器（反复出现整站 503 的根因）。
    unoptimized: true,
    deviceSizes: [64, 112, 160, 256, 384],
    imageSizes: [64, 112, 160, 256],
  },
  // 卡牌总包用「内容哈希 ?v= 做缓存击穿」，可永久缓存：
  // 内容不变浏览器走磁盘缓存（零网络），改卡后哈希变会自动重新下载。
  async headers() {
    const immutable = { key: "Cache-Control", value: "public, max-age=31536000, immutable" };
    return [
      { source: "/data/allCards.json", headers: [immutable] },
      // 卡图与缩略图文件名稳定、内容不变,可永久缓存,重复浏览走磁盘缓存秒出
      { source: "/cards/:path*", headers: [immutable] },
      { source: "/cards-thumb/:path*", headers: [immutable] },
      { source: "/cards-webp/:path*", headers: [immutable] },
      { source: "/sprites-thumb/:path*", headers: [immutable] },
      { source: "/status-icons/:path*", headers: [immutable] },
    ];
  },
};

export default nextConfig;
