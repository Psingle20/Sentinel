# Business and technical contracts

Contracts that cross a service boundary. The C# records in `packages/Sentinel.Contracts` are the source of truth for integration messages ([ADR-012](../decisions/ADR-012-event-semantics.md)); these documents explain them.

- [Integration messages](integration-messages.md): every event and command, its producer, consumers, stream and schema version.
- [Synchronous contracts](synchronous-contracts.md): bounded request/response calls between services.
- [Product analytics](product-analytics.md): the approved usage events, their allowed values and how they are counted.
- [Schema-change template](schema-change-template.md): the record required for any contract change.

Compatibility is enforced by the snapshot tests in `packages/Sentinel.Contracts/tests/Sentinel.Contracts.Tests`. A change that alters a committed snapshot needs a schema-change record.
