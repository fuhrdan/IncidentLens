import { Component, inject, signal } from '@angular/core';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-login',
  template: `<main class="auth-shell"><section class="auth-card" aria-labelledby="login-heading">
    <p class="auth-eyebrow">INCIDENTLENS · 1.0.0</p>
    <h1 id="login-heading">Incident command, together.</h1>
    <p>Sign in using your organization's identity provider to access its incident workspace.</p>
    @if (message()) { <p class="auth-error" role="alert">{{ message() }}</p> }
    <button type="button" [disabled]="busy()" (click)="signIn()">{{ busy() ? 'Connecting…' : 'Sign in' }}</button>
    <p class="auth-note">Access requires a Viewer or Commander role and an assigned tenant.</p>
  </section></main>`,
  styles: [`:host{display:block;min-height:100vh;background:#071522;color:#e8f5fa;font-family:system-ui}
    .auth-shell{min-height:100vh;display:grid;place-items:center;padding:24px}
    .auth-card{max-width:520px;padding:48px;border:1px solid #275467;border-radius:20px;background:#0c2435}
    .auth-eyebrow{color:#56d5cd;letter-spacing:.14em;font-size:12px}
    h1{font-size:2.5rem;line-height:1.1}p{line-height:1.6;color:#abc3d0}
    button{background:#43e3cc;border:0;border-radius:8px;padding:14px 24px;font-weight:700;cursor:pointer}
    button:disabled{opacity:.55}.auth-error{color:#ffc1c1}.auth-note{font-size:13px}`],
})
export class Login {
  private readonly auth = inject(AuthService);
  readonly busy = signal(false);
  readonly message = signal('');
  async signIn(): Promise<void> {
    this.busy.set(true); this.message.set('');
    try { await this.auth.login(); }
    catch (error) { this.message.set(error instanceof Error ? error.message : 'Unable to start sign-in.'); this.busy.set(false); }
  }
}
