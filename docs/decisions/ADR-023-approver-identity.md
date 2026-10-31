# ADR-023: Approver identity

- Status: Deferred until production (2026-10-31). The local deployment has no identity provider, so this is decided with the production deployment environment; the proposal below is the starting point for that decision.
- Date: 2026-10-30

## Context

An approval is the one human act that lets Sentinel change a production system ([ADR-006](ADR-006-remediation-control-path.md)). The Gateway signs it with the approver's identity, and Executor trusts that signature. Who the approver is therefore matters as much as the signature itself.

The Gateway half exists. Outside Development it accepts JWT bearer tokens from a configured authority, reads roles and tenants from configurable claims, and authorizes every endpoint by the roles `responder`, `approver` and `operator` ([IdentityExtensions](../../services/Sentinel.Gateway/src/Sentinel.Gateway.Api/Identity/IdentityExtensions.cs)). The web app half does not: Sentinel.Web signs nobody in. It calls the Gateway from the server as one development identity named in `SENTINEL_DEV_USER`, so every person using one web app acts as the same user. Phase 08 cannot close until a real person signs in, and nothing yet says what an approver must prove at the moment of approving.

## Constraints

- Approvals stay signed by the Gateway and verified by Executor ([ADR-006](ADR-006-remediation-control-path.md)); this decision only says whose identity they carry.
- The web app talks only to the Gateway ([ADR-002](ADR-002-frontend-design-system.md)); it holds no service credentials.
- Sentinel stays neutral about the identity provider: any standards-based provider an organization already runs must work.
- Development identities stay possible locally, and only in Development.
- Tenancy comes from the identity: a person sees and approves only within their tenants.

## Considered options

1. **OpenID Connect with Sentinel.Web as the backend for its frontend.** The web app's server runs the authorization code flow with PKCE against the organization's provider, keeps the tokens in an encrypted, HTTP-only session cookie, and sends the person's access token to the Gateway on every call. The browser never holds a token.
2. **OpenID Connect with the Gateway holding the session.** The Gateway runs the flow and issues its own cookie; the web app forwards it. One fewer component handles tokens, but the Gateway takes on browser sessions and CSRF defence, and its API stops being a plain bearer-token API.
3. **Tokens in the browser.** A single-page flow keeps the access token in the browser and calls the Gateway directly. Simplest, but it exposes tokens to script and breaks the rule that the web app's server is the only caller.
4. **SAML.** Supported by many organizations, but it fits server-rendered sessions poorly and adds a second protocol beside the JWTs the Gateway already validates.

## Decision

Proposed: option 1.

- **Sign-in.** Sentinel.Web signs people in with OpenID Connect, authorization code with PKCE, against the provider named in its configuration. The session is an encrypted, HTTP-only, `SameSite=Lax` cookie holding the tokens; tokens never reach browser script. The server refreshes the access token before it expires and signs the person out when it cannot.
- **Calls.** Every Gateway call from the web app's server carries that person's access token. `SENTINEL_DEV_USER` remains only for Development and is refused at start-up in any other environment.
- **Roles and tenants.** They come from the provider's claims, mapped by the Gateway's existing `RoleClaim` and `TenantClaim` settings, so groups in the organization's directory decide who is a responder, an approver or an operator, per tenant.
- **What an approval requires.** Approving, rejecting and revoking a plan require the `approver` role and a recent, strong sign-in: the token's `auth_time` is at most 15 minutes old and its `amr` includes multi-factor authentication. Otherwise the Gateway answers 401 with a step-up challenge, and the web app sends the person through the provider again before the approval is retried with the same idempotency key. Both limits are Gateway configuration.
- **Accountability.** The approval attestation carries the provider's stable subject and issuer, and the audit log the display name beside them, so a renamed person stays the same approver.

## Consequences

### Positive

- Every approval names a real person who signed in recently with a second factor, which is what Executor's trust in the signature assumes.
- Organizations reuse their own provider, groups and offboarding: removing someone from a group removes their approval rights at their next token refresh.
- The Gateway's API is unchanged; only the web app and the approval checks gain code.

### Negative

- The web app's server now holds user tokens and a session secret, and needs session storage that survives a restart of the web app.
- Step-up sign-in adds a step to every approval made more than 15 minutes after signing in, during an incident.
- Local journey tests need either the development identity, as today, or a local OpenID Connect provider, which adds a container to the AppHost.

## Follow-up work

- Sign-in, session and token refresh in Sentinel.Web, with the development identity confined to Development (Phase 08).
- The `auth_time` and `amr` checks and the step-up response on the approval endpoints, with tests (Phase 08).
- The subject and issuer in the approval attestation and audit records (Phase 08, a contract version change under [ADR-012](ADR-012-event-semantics.md)).
- An optional local OpenID Connect provider in the AppHost for journey tests with several people, for example an approver and an operator in one run (Phase 08).
- A runbook for granting and removing approver rights, and for a provider outage, when the web app cannot sign anyone in (Phase 09).

## Revisit criteria

- An organization needs approvals from people outside its provider, for example a vendor on call; then a second provider or a federation must be decided.
- High-risk actions need two approvers; then the approval aggregate, not only identity, changes.
