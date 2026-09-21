# v1.0.0 browser sign-in: authorization code with PKCE

## Overview

The Angular browser is a **public OAuth client**, not a confidential client.
It uses OIDC discovery, authorization code + PKCE/S256 and a one-use state
transaction stored in sessionStorage. The access token lives **only in memory**
and is removed on logout or API 401. No client secret, refresh token or local
storage bearer token is used. The API, not the browser, validates JWT signature,
issuer, audience, expiration, tenant ID and role. A page reload requires login
again. The authorization callback removes the code from browser history before
exchanging it; no token is accepted from a URL fragment.

## Identity-provider setup (required for real deployment)

1. Register a **public single-page application (SPA)** named `incidentlens-spa`
   or use your provider's equivalent public-client setting. Do not assign a
   client secret to the web application. Enable authorization-code flow with
   PKCE S256; disable implicit flow and resource-owner-password credentials.
2. Register the EXACT redirect URI `https://YOUR_HOST/auth/callback` and set
   the allowed web origin to `https://YOUR_HOST`. Configure CORS for your SPA
   origin on the provider token endpoint. If its browser token endpoint does
   not support cross-origin exchange, use a reviewed BFF architecture instead;
   **do not** weaken browser security or embed an identity-provider secret.
3. Configure an access-token audience corresponding to `OIDC_AUDIENCE` and
   issuer exactly matching `OIDC_AUTHORITY`, with HTTPS and trusted certificate.
   The access token MUST contain the normalized `tenant_id` (or configured
   equivalent) and role `Viewer` or `Commander` under the configured role claim.
   Some providers issue these only in ID tokens by default; explicitly map
   them into the **access token** for IncidentLens API. ID token alone does not
   grant API access. Verify actual JWT contents on a safe staging account.
4. Configure `OIDC_CLIENT_ID`, `OIDC_AUTHORITY`, `OIDC_AUDIENCE` and optionally
   `OIDC_SCOPE` (`openid profile` by default), `ROLE_CLAIM_TYPE` and
   `TENANT_CLAIM_TYPE`. Never request `offline_access`; this client does not
   persist or rotate refresh tokens.
5. Test a Viewer, Commander, user without tenant claim, and another tenant.
   An authenticated user without correct role or tenant receives 403; an
   unauthenticated request receives 401. Validate incident access and exports.
6. During a session the browser calls `/api/auth/client-config` to fetch only
   public issuer/client-id/scope values. Production startup rejects a missing
   HTTPS authority, audience or client ID. The development-only demo-token route
   is unavailable in production and is not used by the OIDC flow.

## UX and security limitations

- Sign-in begins at `/login`; the identity provider redirects to
  `/auth/callback`. Failed/replayed/expired state is rejected and cleared.
- Bearer is provided to same-origin API requests and authenticated SignalR
  connections, never to third-party origins. API 401 clears the browser session.
- Access tokens are not refreshed: expiry requires another interactive login;
  long-lived sessions/re-authentication UX remain possible post-GA improvements.
- Logout clears local credentials; it does NOT revoke an already-issued access
  token or sign the user out of the external identity provider. Configure short
  provider token lifetimes; revoke through the provider if needed.
- Use HTTPS, restrictive content security policy, strict input handling and
  continuous dependency review to reduce risk of malicious same-origin JS.
  In-memory storage limits persistence, but cannot protect against XSS.
- Role-based API policies and tenant isolation are the actual security boundary.
  The Angular route guard only prevents loading the dashboard before login.

## Development

The separate demo application (`environment.ts` with `demoMode: true`) stays
self-contained. For local full stack, set `demoMode: false`; start the API in
Development without an external authority, then select Sign in at `/login`.
The button requests the development-only token endpoint automatically. No
console paste/sessionStorage token is needed. NEVER deploy with Development
runtime settings, a demo JWT secret, or sample data.
