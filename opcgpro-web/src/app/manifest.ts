import type { MetadataRoute } from "next";

export default function manifest(): MetadataRoute.Manifest {
  return {
    name: "GrandUMI",
    short_name: "GrandUMI",
    description: "One Piece Card Game Online",
    start_url: "/",
    display: "fullscreen",
    background_color: "#030712",
    theme_color: "#030712",
    icons: [
      {
        src: "/icon.svg",
        sizes: "any",
        type: "image/svg+xml",
      },
    ],
  };
}
