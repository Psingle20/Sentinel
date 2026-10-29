# Product analytics

Sentinel counts how its product is used, and nothing more (Phase 08, deliverable 6). The catalog below is the whole contract: the Gateway accepts only these events, each property only with one of its listed values, and keeps daily counts per event and values. It never stores an individual event, who sent it, which incident or tenant it concerned, or any free text.

## Catalog

| Event | Properties and allowed values | Sent by Sentinel.Web when |
| --- | --- | --- |
| `incident.viewed` | `status`: an incident status. `severity`: `critical`, `high`, `medium`, `low` | An incident page is shown. |
| `approval.dialog-opened` | `action`: `approve`, `reject`, `revoke` | An approver opens the approve, reject or revoke dialog. |
| `postmortem.viewed` | `status`: an incident status | A postmortem page is shown. |
| `narrative.saved` | `outcome`: `saved`, `conflict`, `failed` | A postmortem narrative save returns. `conflict` means someone else saved first. |
| `replay.requested` | `outcome`: `reproduced`, `differs`, `failed` | A domain replay returns. |
| `live.state-changed` | `state`: `live`, `reconnecting`, `offline` | The live-update connection changes state after it was first live, so a page load is not counted as a reconnect. |
| `admin.viewed` | `area`: `overview`, `policy`, `runtime`, `models`, `sources`, `api` | A control-plane page is shown. |
| `configuration.published` | `area`: as for `admin.viewed`. `outcome`: `published`, `refused` | A policy or configuration publish returns. |

The incident statuses are `detected`, `investigating`, `awaitingApproval`, `remediating`, `verifying`, `resolved` and `escalated`.

## Path and privacy

1. The browser posts `{ name, properties }` to the web app's own `/api/analytics` route, by `navigator.sendBeacon` or a keep-alive `fetch`. Sending is fire and forget, so analytics never delays or breaks what the responder is doing.
2. The route drops the event when the browser sends Global Privacy Control (`Sec-GPC: 1`) or Do Not Track (`DNT: 1`), refuses bodies over 2 KB, and otherwise forwards it with the server-side identity to the Gateway's `POST /api/v1/analytics`, which requires the responder role.
3. The Gateway checks the event against its catalog and increments one row of `api.analytics_daily`, keyed by day, event and the properties in name order (for example `area=models;outcome=refused`). An event outside the catalog, an unknown property or a value outside its list is refused with `Analytics.UnknownEvent` or `Analytics.UnknownProperty`.
4. Operators read the counts of the last 1 to 366 days with `GET /api/v1/admin/analytics?days=`; the control-plane overview shows the last 30.

## Changing the catalog

The catalog is a reviewed code change in three places that must agree: [AnalyticsCatalog](../../services/Sentinel.Gateway/src/Sentinel.Gateway.Application/Analytics/AnalyticsCatalog.cs) in the Gateway, the event types in [track.ts](../../apps/Sentinel.Web/src/features/analytics/track.ts), and the stand-in catalog in the web app's [mock API](../../apps/Sentinel.Web/tests/support/mock-api.mjs). Update this document with them. Never add a property that identifies a person, an incident, a tenant or an environment, and never one that takes free text.
