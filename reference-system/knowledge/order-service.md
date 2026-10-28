---
service: order-service
---
# order-service runbook

order-service looks up the cart's order history in its database and records an idempotency key in Redis. A healthy lookup takes about 30 ms.

## Symptoms

A lookup that slows steadily after a release usually means the release added an unindexed query: latency grows with the data it scans. Callers with short timeouts, such as checkout-service, start failing once the lookup passes their timeout.

## Remediation

Roll order-service back to the previous release. Latency returns to normal immediately; callers recover within a minute.
