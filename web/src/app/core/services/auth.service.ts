import { Injectable, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { environment } from '../../../environments/environment';

interface ClientConfig { mode: 'oidc' | 'development'; authority: string; clientId: string; scope: string; }
interface Discovery { issuer: string; authorization_endpoint: string; token_endpoint: string; }
interface PendingLogin { state: string; verifier: string; redirectUri: string; createdAt: number; }
interface TokenResponse { access_token: string; token_type: string; expires_in: number; }

const TRANSACTION_KEY = 'incidentlens_oidc_transaction';
const BASE64_URL = (bytes: Uint8Array): string =>
  btoa(Array.from(bytes, byte => String.fromCharCode(byte)).join(''))
    .replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '');

/** OAuth public SPA client: authorization code/PKCE, short-lived in-memory bearer only.
 * Access tokens are NOT validated by this browser; the API validates signature, issuer,
 * audience, lifetime, tenant claim and role. Never treat unverified JWT claims as access.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly router = inject(Router);
  private configuration?: ClientConfig;
  private discovery?: Discovery;
  private accessToken = '';
  private expiresAt = 0;
  private expiryTimer?: ReturnType<typeof setTimeout>;
  readonly error = signal('');
  readonly authenticated = signal(environment.demoMode);
  readonly operatorName = signal('');

  token(): string {
    if (Date.now() >= this.expiresAt) {
      if (this.accessToken) this.clearSession();
      return '';
    }
    return this.accessToken;
  }

  async config(): Promise<ClientConfig> {
    if (this.configuration) return this.configuration;
    const response = await fetch(`${environment.apiBaseUrl}/auth/client-config`,
      { cache: 'no-store', credentials: 'omit' });
    if (!response.ok) throw new Error('Browser authentication is not configured on this server.');
    const config = await response.json() as ClientConfig;
    if (config.mode !== 'development' && config.mode !== 'oidc') throw new Error('Invalid login configuration.');
    if (config.mode === 'development' && environment.production) throw new Error('Development login is forbidden in production.');
    if (config.mode === 'oidc' && (!config.clientId || !config.authority)) throw new Error('Incomplete login configuration.');
    this.configuration = config;
    return config;
  }

  private async oidc(): Promise<Discovery> {
    if (this.discovery) return this.discovery;
    const config = await this.config();
    const authority = config.authority.replace(/\/$/, '');
    const issuer = new URL(authority);
    if (issuer.protocol !== 'https:' || issuer.username || issuer.password || issuer.hash || issuer.search)
      throw new Error('OIDC authority must be a valid HTTPS issuer.');
    const url = `${authority}/.well-known/openid-configuration`;
    const response = await fetch(url, { cache: 'no-store', credentials: 'omit' });
    if (!response.ok) throw new Error('Unable to load identity provider discovery.');
    const discovery = await response.json() as Discovery;
    // Metadata endpoints may use a different host, but never an insecure scheme.
    if (discovery.issuer !== authority ||
        ![discovery.authorization_endpoint, discovery.token_endpoint].every(endpoint => {
          try { const parsed = new URL(endpoint); return parsed.protocol === 'https:' && !parsed.username && !parsed.password; }
          catch { return false; }
        })) throw new Error('Untrusted identity provider metadata.');
    this.discovery = discovery;
    return discovery;
  }

  async login(): Promise<void> {
    this.error.set('');
    const config = await this.config();
    if (config.mode === 'development') {
      const response = await fetch(`${environment.apiBaseUrl}/auth/demo-token`, { method: 'POST', cache: 'no-store' });
      if (!response.ok) throw new Error('Local development login failed.');
      const token = await response.json() as { accessToken: string };
      // Development tokens are valid for eight hours; memory only.
      this.acceptToken({ access_token: token.accessToken, token_type: 'Bearer', expires_in: 8 * 3600 });
      await this.router.navigateByUrl('/');
      return;
    }
    if (window.location.protocol !== 'https:' && window.location.hostname !== 'localhost')
      throw new Error('Production sign-in requires HTTPS.');
    const oidc = await this.oidc();
    const verifier = BASE64_URL(crypto.getRandomValues(new Uint8Array(32)));
    const challenge = BASE64_URL(new Uint8Array(await crypto.subtle.digest('SHA-256',
      new TextEncoder().encode(verifier))));
    const state = BASE64_URL(crypto.getRandomValues(new Uint8Array(32)));
    const redirectUri = `${window.location.origin}/auth/callback`;
    const pending: PendingLogin = { state, verifier, redirectUri, createdAt: Date.now() };
    sessionStorage.setItem(TRANSACTION_KEY, JSON.stringify(pending));
    const url = new URL(oidc.authorization_endpoint);
    url.searchParams.set('response_type', 'code');
    url.searchParams.set('client_id', config.clientId);
    url.searchParams.set('redirect_uri', redirectUri);
    url.searchParams.set('scope', config.scope || 'openid profile');
    url.searchParams.set('state', state);
    url.searchParams.set('code_challenge', challenge);
    url.searchParams.set('code_challenge_method', 'S256');
    window.location.assign(url.toString());
  }

  async completeLogin(parameters: URLSearchParams): Promise<void> {
    // Remove authorization code immediately so URLs, browser history and subsequent
    // app navigation never retain it; state and verifier can be used just once.
    window.history.replaceState(null, '', '/auth/callback');
    const raw = sessionStorage.getItem(TRANSACTION_KEY);
    sessionStorage.removeItem(TRANSACTION_KEY);
    const state = parameters.get('state');
    const code = parameters.get('code');
    if (!raw || !state || !code || parameters.has('error')) throw new Error('Sign-in was cancelled or expired.');
    let pending: PendingLogin;
    try { pending = JSON.parse(raw) as PendingLogin; }
    catch { throw new Error('Invalid sign-in transaction.'); }
    if (pending.state !== state || pending.createdAt > Date.now() ||
        Date.now() - pending.createdAt > 10 * 60_000 ||
        pending.redirectUri !== `${window.location.origin}/auth/callback` ||
        !/^[A-Za-z0-9_-]{43,128}$/.test(pending.verifier))
      throw new Error('Sign-in state or PKCE verifier is invalid.');
    const config = await this.config();
    if (config.mode !== 'oidc') throw new Error('OIDC is not enabled.');
    const oidc = await this.oidc();
    const body = new URLSearchParams({ grant_type: 'authorization_code', code,
      redirect_uri: pending.redirectUri, client_id: config.clientId, code_verifier: pending.verifier });
    const response = await fetch(oidc.token_endpoint, { method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' }, body,
      cache: 'no-store', credentials: 'omit' });
    if (!response.ok) throw new Error('Identity provider rejected the authorization code.');
    this.acceptToken(await response.json() as TokenResponse);
    await this.router.navigateByUrl('/');
  }

  private acceptToken(token: TokenResponse): void {
    if (!token.access_token || token.token_type?.toLowerCase() !== 'bearer' ||
        !Number.isFinite(token.expires_in) || token.expires_in <= 30)
      throw new Error('Identity provider did not return a usable access token.');
    this.accessToken = token.access_token;
    const validityMs = Math.min((token.expires_in - 30) * 1000, 86_400_000);
    this.expiresAt = Date.now() + validityMs;
    if (this.expiryTimer) clearTimeout(this.expiryTimer);
    this.expiryTimer = setTimeout(() => {
      this.clearSession();
      void this.router.navigateByUrl('/login');
    }, validityMs);
    this.authenticated.set(true);
  }

  clearSession(): void {
    if (this.expiryTimer) clearTimeout(this.expiryTimer);
    this.expiryTimer = undefined;
    this.accessToken = '';
    this.expiresAt = 0;
    this.authenticated.set(environment.demoMode);
    this.operatorName.set('');
    sessionStorage.removeItem(TRANSACTION_KEY);
    // Older versions stored tokens here; never carry those tokens into v1.0.
    sessionStorage.removeItem('incidentlens_access_token');
  }

  async logout(): Promise<void> { this.clearSession(); await this.router.navigateByUrl('/login'); }
}
