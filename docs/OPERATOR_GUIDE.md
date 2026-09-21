# IncidentLens v1.0.0 — operator handbook

## Access and incident coordination

Access `https://YOUR_HOST/login`, select **Sign in**, and authenticate through
your organization's identity provider. Accounts need `Viewer` or `Commander`
and a valid tenant assignment. Viewer can see the tenant incident queue;
Commander is required for changes, audit/evidence and retention. Reloading the
page may require signing in again; this version does not store bearer tokens
across page reloads. Never put access tokens in a browser console or ticket.

The dashboard lists incidents for your tenant, with text, severity, status and
owner-team filters. Select an incident to review severity, impact, service,
assignee, responders, tags and timeline. Active SignalR presence indicates
who is viewing the same incident in this API instance. On disconnect, refresh
if you suspect stale information. The UI rejects concurrent changes with a
version conflict and reloads the latest incident record; review before retrying.

## Commander workflow

1. Declare an incident with an accurate title, impact summary, severity,
   service, owner team and assignee. Use operational facts, not secrets.
2. Move through Investigating → Identified → Monitoring → Resolved when the
   evidence justifies it. Update command ownership and responders as needed.
3. Use timeline notes for decisions, mitigations and chronology. Supported
   structured commands are described in `docs/API.md`.
4. Review reliability signals and maintenance windows. Acknowledgement is not
   the same as resolution; promote a signal to an incident when warranted.
   Service-objective evaluation is **operator-triggered**, not a background job.
5. Complete a postmortem with contributing facts, root cause, detection,
   resolution, lessons learned and action items with owners and due dates.
6. Export JSON/CSV evidence for authorized incident reviews. Treat exports as
   sensitive: they contain incident and audit details, so store them securely.

## Operational troubleshooting

- Sign in loops: confirm exact callback URI, issuer, SPA CORS and PKCE S256.
- 401: token absent or expired; sign in again. 403: check role and tenant in
  the **access token**, not ID token. Contact the identity administrator.
- 429: honor Retry-After, avoid automatic mutation retries.
- API unavailable: check `/health/live` and `/health/ready` and tell an admin
  the `X-Correlation-ID` from a failed API request. Do not share bearer tokens.
- Updates conflicting: reload and compare latest record before resubmitting.
- Realtime disconnected: use normal refresh, confirm ingress websocket routing.

This version operates with a **single API replica**. Retention cleanup is off
unless enabled by an administrator after a reviewed backup and recovery drill.
