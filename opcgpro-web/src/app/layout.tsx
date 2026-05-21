import type { Metadata, Viewport } from "next";
import NetProvider from "@/components/NetProvider";
import "./globals.css";

export const metadata: Metadata = {
  title: "GrandUMI",
  description: "One Piece Card Game Online",
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  maximumScale: 1,
  userScalable: false,
  viewportFit: "cover",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="zh" className="h-full">
      <head>
        {/* 移动端横屏锁定提示 */}
        <script
          dangerouslySetInnerHTML={{
            __html: `
              (function() {
                var style = document.createElement('style');
                style.id = 'landscape-lock-style';
                style.textContent = '';
                document.head.appendChild(style);
                function checkOrientation() {
                  var isPortrait = window.innerWidth < window.innerHeight;
                  var isSmall = window.innerWidth < 768;
                  if (isPortrait && isSmall) {
                    style.textContent = 'html::before { content: "请将设备横屏使用"; position: fixed; inset: 0; z-index: 99999; background: #111; color: #fff; display: flex; align-items: center; justify-content: center; font-size: 18px; font-weight: bold; }';
                  } else {
                    style.textContent = '';
                  }
                }
                checkOrientation();
                window.addEventListener('resize', checkOrientation);
                window.addEventListener('orientationchange', function() { setTimeout(checkOrientation, 100); });
              })();
            `,
          }}
        />
      </head>
      <body className="min-h-full bg-gray-950 text-white antialiased">
        <NetProvider>{children}</NetProvider>
      </body>
    </html>
  );
}
