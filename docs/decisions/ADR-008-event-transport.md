# ADR-008: Event transport

- Status: Accepted
- Date: 2026-10-28

## Context

Every step of the walking skeleton crosses a service boundary, so Phase 00 must select the durable transport for integration events. Phase 02 requires at-least-once delivery with idempotent consumers, dead-lettering and backpressure. Replay is not a transport requirement: the incident event stream is the replay source for decisions ([ADR-004](ADR-004-runtime-state-ownership.md)), and Ingestion's observation store is the replay source for telemetry.

Local footprint matters, because a full local run already includes five services, PostgreSQL, Temporal, an observability stack and the reference system.

## Constraints

- Delivery survives a consumer being offline.
- Competing consumers within a service, with explicit acknowledgment.
- Dead-lettering of poison messages with the failure reason.
- No custom broker (architecture §63).
- A domain write and its outgoing event cannot diverge (architecture §37).

## Considered options

1. Redis Pub/Sub.
2. Redis lists used as queues.
3. Redis Streams with consumer groups.
4. RabbitMQ.
5. NATS JetStream.
6. Kafka.
7. PostgreSQL as the transport.

## Decision

Use **Redis Streams with consumer groups**, implemented by `CleanArchitecture.BuildingBlocks.Infrastructure.Redis` ([building-blocks evolution](../implementation/building-blocks-evolution.md)).

- Each service writes integration events to a transactional outbox in its own PostgreSQL database ([ADR-009](ADR-009-persistence-baseline.md)). The outbox dispatcher publishes to Redis after the domain transaction commits.
- Consumers read through consumer groups, record processed message IDs in an inbox, and acknowledge only after handling succeeds.
- Messages left pending by a crashed consumer are reclaimed after an idle timeout. Messages exceeding the maximum delivery count move to a `<stream>.dlq` stream with their failure reason.
- Stream names follow the topic families in architecture §56, for example `sentinel.observations` and `sentinel.incidents`. Streams are trimmed by length, with retention at least as long as the longest tolerated consumer outage.
- Sentinel's messaging Redis is a dedicated instance, separate from any Redis in the reference system. Scenario `S2` deliberately saturates the reference Redis, and a shared instance would take Sentinel's messaging down with it.
- The messaging instance runs with AOF persistence and `appendfsync always`, so a message the outbox marked as published survives a Redis restart.
- Redis Pub/Sub is not used for integration events, because it drops messages for offline subscribers. It may carry best-effort notifications, such as live UI hints, only where loss is acceptable and the consumer can catch up from a query API.
- Redis lists are not used, because they lack consumer-group tracking and reclaiming.

## Consequences

### Positive

- A light local footprint and a first-class Aspire integration.
- Consumer groups, acknowledgment and reclaiming cover at-least-once delivery without a heavier broker.
- The outbox and inbox make delivery safe across crashes and duplicates.

### Negative

- Stream retention is bounded by memory, so trimming must be configured deliberately.
- Ordering is guaranteed only within a stream.
- Pending-entry reclaiming and dead-lettering are Sentinel-owned code in the building blocks, not broker features.
- `appendfsync always` limits write throughput. That is acceptable for control-plane traffic but must be measured before Phase 02 ingestion volumes.
- Asynchronous replication in a highly available Redis deployment can lose recently acknowledged writes on failover. Phase 09 must address this before production.
- Server licensing: recent Redis releases are source-available, with AGPLv3 offered from Redis 8, while Valkey is a BSD-licensed fork with compatible Streams. The server image is chosen when versions are pinned.

## Follow-up work

- Implement the Redis Streams adapter, the outbox dispatcher and the inbox in the building blocks (Stage E).
- Configure the messaging Redis in the Aspire AppHost ([ADR-010](ADR-010-local-orchestration.md)) with AOF persistence. Done 2026-10-28: `appendonly yes` and `appendfsync always`, verified against the running container, alongside Aspire's default RDB snapshots.
- Measure throughput with `appendfsync always` before Phase 02 ingestion load.

## Revisit criteria

Revisit if ingestion throughput or retention outgrows memory-bound streams, if Phase 09 cannot make failover durable enough, or if a requirement emerges for long-term broker-side replay.
