import type { Metadata, Viewport } from "next";
import NetProvider from "@/components/NetProvider";
import AudioProvider from "@/components/audio/AudioProvider";
import LayoutSettingsProvider from "@/components/home/LayoutSettingsProvider";
import LanguageProvider from "@/i18n/LanguageProvider";
import "./globals.css";

export const metadata: Metadata = {
  title: "GrandUMI",
  description: "One Piece Card Game Online",
  appleWebApp: {
    capable: true,
    title: "GrandUMI",
    statusBarStyle: "black-translucent",
  },
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  viewportFit: "cover",
  colorScheme: "dark",
  themeColor: "#030712",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="zh-CN" className="h-full">
      <body className="min-h-full bg-gray-950 text-white antialiased">
        <LanguageProvider>
          <AudioProvider>
            <LayoutSettingsProvider>
              <NetProvider>{children}</NetProvider>
            </LayoutSettingsProvider>
          </AudioProvider>
        </LanguageProvider>
      </body>
    </html>
  );
}
