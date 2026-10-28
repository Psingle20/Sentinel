# Sentinel product architecture diagram

## Source

- Architecture specification: [`docs/architecture/architecture.md`](../../../docs/architecture/architecture.md)
- Archify candidate: [`candidate.json`](candidate.json)

The diagram is a product-level system view. It preserves the architecture's main operational loop, control plane, provider boundaries, durable runtime, Meta model integration, evidence and causal reasoning, adversarial verification, consensus, policy-controlled remediation, recovery verification, incident memory, replay, and cross-cutting scale and safety concerns.

## Regeneration

Generated with Archify 3.0.1 using showcase quality:

```powershell
node "C:\Users\ingle\.codex\skills\archify\bin\archify.mjs" finalize architecture candidate.json sentinel-system-architecture.html --quality showcase --json
```

Run the command from this directory. The checked HTML is [`sentinel-system-architecture.html`](sentinel-system-architecture.html).

## Validation evidence

- Finalize summary: [`sentinel-system-architecture.finalize-summary.json`](sentinel-system-architecture.finalize-summary.json)
- Full finalize receipt: [`sentinel-system-architecture.finalize.json`](sentinel-system-architecture.finalize.json)
- Browser-check receipt: [`sentinel-system-architecture.browser-check.json`](sentinel-system-architecture.browser-check.json)

The finalized artifact passed showcase validation, deterministic delivery, strict provenance checks, and automated browser checks. Light and dark desktop captures were also inspected for route clarity and readability; the visual-review contact sheet from that inspection was not retained with this artifact.
