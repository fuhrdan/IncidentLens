import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, of, switchMap, timer } from 'rxjs';
import { AuthService } from '../../core/services/auth.service';
import { DashboardService } from './dashboard.service';
import { DashboardOverview } from './dashboard.models';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'app-command-center',
  imports: [RouterLink, DatePipe, DecimalPipe],
  templateUrl: './command-center.html',
  styleUrl: './command-center.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CommandCenter {
  private readonly destroyRef = inject(DestroyRef);
  private readonly dashboard = inject(DashboardService);
  readonly auth = inject(AuthService);
  readonly demoMode = environment.demoMode;
  readonly environmentLabel = environment.demoMode ? 'Demo' : environment.production ? 'Production' : 'Development';
  readonly overview = signal<DashboardOverview | null>(null);
  readonly error = signal('');
  readonly loading = signal(true);
  readonly search = signal('');
  readonly visibleIncidents = computed(() => {
    const text = this.search().trim().toLowerCase();
    return this.overview()?.activeIncidents.filter(row => !text ||
      [row.id, row.title, row.service, row.ownerTeam, row.assignee, row.status]
        .some(value => value.toLowerCase().includes(text))) ?? [];
  });

  constructor() {
    // Bounded polling until tenant-scoped dashboard invalidation is implemented.
    timer(0, 30_000).pipe(
      switchMap(() => this.dashboard.overview(7).pipe(catchError(() => {
        this.error.set('Unable to refresh the dashboard. Previously loaded data, if any, may be stale.');
        return of(null);
      }))),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(value => {
      this.loading.set(false);
      if (value) { this.overview.set(value); this.error.set(''); }
    });
  }

  updateSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
  }

  /** Re-read the API now; routine updates continue every 30 seconds. */
  refresh(): void {
    this.dashboard.overview(7).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: value => { this.overview.set(value); this.error.set(''); },
      error: () => this.error.set('Dashboard refresh failed; showing the last successful snapshot.'),
    });
  }

  duration(iso: string): string {
    const minutes = Math.max(0, Math.floor((Date.now() - new Date(iso).getTime()) / 60_000));
    return minutes < 60 ? `${minutes}m` : `${Math.floor(minutes / 60)}h ${minutes % 60}m`;
  }
}
