import { ApiUnavailableError, openEventStream } from "@/lib/api/server";

// Live updates for the browser (Phase 08): Sentinel.Gateway's server-sent event stream, proxied with
// the server-side identity so the browser never talks to Api directly. Reading the request makes
// this handler run per request; the browser's EventSource sends Last-Event-ID when it reconnects,
// and Api resumes after it.
export async function GET(request: Request): Promise<Response> {
  // The browser's own reconnects send Last-Event-ID; a reconnect after the browser gave up names it in ?after.
  const resume = request.headers.get("last-event-id") ?? new URL(request.url).searchParams.get("after");
  let upstream: Response;
  try {
    upstream = await openEventStream(resume && /^\d+$/.test(resume) ? resume : null, request.signal);
  } catch (error) {
    const reason = error instanceof ApiUnavailableError || error instanceof Error ? error.message : "Sentinel.Gateway is unreachable.";
    return new Response(reason, { status: 503, headers: { "Retry-After": "5" } });
  }

  if (!upstream.ok || !upstream.body) {
    return new Response(`Sentinel.Gateway returned ${upstream.status}.`, { status: upstream.status === 200 ? 502 : upstream.status });
  }

  return new Response(upstream.body, {
    headers: {
      "Content-Type": "text/event-stream",
      "Cache-Control": "no-cache, no-transform",
      Connection: "keep-alive",
    },
  });
}
