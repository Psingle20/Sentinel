import { recordAnalytics } from "@/lib/api/server";

const maxBody = 2_048;

// Product analytics, forwarded to Sentinel.Gateway with the server-side identity so the browser never
// talks to Api directly. A browser that asks not to be tracked (Global Privacy Control or Do Not
// Track) is honoured here: its events are dropped before they leave the web app.
export async function POST(request: Request): Promise<Response> {
  if (request.headers.get("sec-gpc") === "1" || request.headers.get("dnt") === "1") {
    return new Response(null, { status: 204 });
  }

  const text = await request.text();
  if (text.length > maxBody) {
    return new Response(null, { status: 413 });
  }

  try {
    const event = JSON.parse(text) as { name?: unknown; properties?: unknown };
    if (typeof event.name === "string") {
      await recordAnalytics({ name: event.name, properties: (event.properties ?? undefined) as Record<string, string> | undefined });
    }
  } catch {
    // Analytics is best effort: a malformed event or an unavailable Api is not the responder's problem.
  }

  return new Response(null, { status: 202 });
}
