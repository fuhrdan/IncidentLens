import { Component, OnInit, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-auth-callback',
  template: `<main class="auth-callback" aria-live="polite">
    <h1>Signing in to IncidentLens</h1>
    @if (error()) { <p role="alert">{{ error() }}</p><a href="/login">Return to sign in</a> }
    @else { <p>Completing secure sign-in…</p> }
  </main>`,
  styles: [`.auth-callback{padding:4rem;min-height:100vh;background:#071522;color:#e8f5fa;font-family:system-ui}`],
})
export class AuthCallback implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  readonly error = signal('');
  async ngOnInit(): Promise<void> {
    const params = new URLSearchParams(window.location.search);
    try { await this.auth.completeLogin(params); }
    catch (error) {
      this.auth.clearSession();
      this.error.set(error instanceof Error ? error.message : 'Sign-in failed.');
    }
  }
}
