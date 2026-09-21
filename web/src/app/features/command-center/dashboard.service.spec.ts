import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { provideIncidentGateway } from '../../core/services/incident-gateway';
import { DashboardService } from './dashboard.service';

describe('DashboardService', () => {
  it('derives demo cards and recent incidents from the existing incident gateway', async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideIncidentGateway()],
    });
    const view = await firstValueFrom(TestBed.inject(DashboardService).overview(7));
    expect(view.summary.activeIncidents).toBeGreaterThanOrEqual(0);
    expect(view.summary.criticalIncidents).toBeLessThanOrEqual(view.summary.activeIncidents);
    expect(view.activeIncidents.every(row => row.status !== 'Resolved')).toBe(true);
  });
});
