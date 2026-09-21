import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthService } from './auth.service';

describe('AuthService public-client safety', () => {
  let auth: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    auth = TestBed.inject(AuthService);
    sessionStorage.clear();
  });

  afterEach(() => {
    auth.clearSession();
    vi.unstubAllGlobals();
    sessionStorage.clear();
  });

  it('loads only public identity-provider settings', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: async () => ({
      mode: 'oidc', authority: 'https://identity.example.org',
      clientId: 'public-spa', scope: 'openid profile',
    }) });
    vi.stubGlobal('fetch', fetchMock);
    expect((await auth.config()).clientId).toBe('public-spa');
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[0][0]).toContain('/api/auth/client-config');
    expect(fetchMock.mock.calls[0][1].credentials).toBe('omit');
  });

  it('rejects callback without a one-use PKCE transaction', async () => {
    await expect(auth.completeLogin(new URLSearchParams('code=abc&state=xyz')))
      .rejects.toThrow(/cancelled or expired/);
    expect(auth.token()).toBe('');
  });

  it('rejects mismatched state without contacting the identity provider', async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);
    sessionStorage.setItem('incidentlens_oidc_transaction', JSON.stringify({
      state: 'expected', verifier: 'A'.repeat(43),
      redirectUri: `${window.location.origin}/auth/callback`, createdAt: Date.now(),
    }));
    await expect(auth.completeLogin(new URLSearchParams('code=abc&state=other')))
      .rejects.toThrow(/state or PKCE/);
    expect(sessionStorage.getItem('incidentlens_oidc_transaction')).toBeNull();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('rejects an expired sign-in transaction', async () => {
    sessionStorage.setItem('incidentlens_oidc_transaction', JSON.stringify({
      state: 'expected', verifier: 'A'.repeat(43),
      redirectUri: `${window.location.origin}/auth/callback`, createdAt: Date.now() - 11 * 60_000,
    }));
    await expect(auth.completeLogin(new URLSearchParams('code=abc&state=expected')))
      .rejects.toThrow(/state or PKCE/);
    expect(sessionStorage.getItem('incidentlens_oidc_transaction')).toBeNull();
  });
});
