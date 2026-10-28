---
service: checkout-service
---
# checkout-service runbook

checkout-service writes each cart to Redis through a pool of four connections, then places the order with order-service. Each order call has a 600 ms timeout and is retried up to three times.

## Symptoms

Redis connection errors ("max number of clients reached") mean Redis is out of client slots. Check `redis_connections` grouped by client name: if checkout-service's own count grows far beyond its pool of four, a checkout release is leaking connections; roll that release back. If another client's count grows, checkout is a victim, not the cause.

Slow checkouts with retries usually mean order-service is slow. Compare checkout's time waiting on order-service with order-service's own latency before acting on checkout.

## Remediation

Roll back the most recent checkout release only when the evidence points at checkout itself. Rolling back an innocent release does not restore the SLO and hides the real cause.
