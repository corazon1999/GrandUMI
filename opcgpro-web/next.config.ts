import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  images: {
    deviceSizes: [64, 112, 160, 256, 384],
    imageSizes: [64, 112, 160, 256],
  },
};

export default nextConfig;
