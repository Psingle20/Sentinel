# AWS-emulated reference environment

Configuration for the second reference environment of [Phase E01](../../../docs/implementation/phase-e01-aws-emulated-reference-environment.md): the AWS emulator chosen in [ADR-021](../../../docs/decisions/ADR-021-aws-emulator.md), Floci, pinned to one image digest.

| File | Content |
| --- | --- |
| `emulator.json` | The pinned image, version, platform and digest. The environment conformance tests and, from Stage 1, the AppHost read it from here and nowhere else, so changing the emulator version is a one-file change. |

The emulator runs only on a developer machine or an isolated CI runner. It mounts the Docker socket so that ECS tasks start real containers, which gives it control of the host's containers; it is never part of a production definition ([ADR-021](../../../docs/decisions/ADR-021-aws-emulator.md#consequences)). Credentials are synthetic: a 12-digit access key selects the emulated account, and the secret is any non-empty value, because the emulator does not verify signatures.
