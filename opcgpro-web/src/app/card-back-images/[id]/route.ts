const IMMUTABLE_CACHE = "public, max-age=31536000, immutable";

function backendBaseUrl(request: Request): string {
  const configured = process.env.CARD_BACK_API_URL?.trim();
  if (configured) return configured.replace(/\/$/, "");

  const forwardedHost = request.headers.get("x-forwarded-host")?.split(",", 1)[0]?.trim();
  const hostname = (forwardedHost || request.headers.get("host") || new URL(request.url).hostname)
    .split(":", 1)[0]
    .toLowerCase();
  return hostname === "test.grand-umi.com" ? "http://127.0.0.1:8081" : "http://127.0.0.1:8080";
}

export const dynamic = "force-dynamic";

export async function GET(
  request: Request,
  { params }: { params: Promise<{ id: string }> },
) {
  const { id } = await params;
  if (!/^[1-9]\d*$/.test(id)) return new Response(null, { status: 404 });

  let upstream: Response;
  try {
    upstream = await fetch(`${backendBaseUrl(request)}/card-back-images/${id}`, { cache: "no-store" });
  } catch {
    return new Response(null, { status: 502 });
  }

  if (!upstream.ok || !upstream.body) return new Response(null, { status: upstream.status });

  return new Response(upstream.body, {
    status: upstream.status,
    headers: {
      "Cache-Control": upstream.headers.get("cache-control") ?? IMMUTABLE_CACHE,
      "Content-Type": upstream.headers.get("content-type") ?? "application/octet-stream",
      "X-Content-Type-Options": "nosniff",
    },
  });
}
