import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { firstValueFrom, of } from 'rxjs';
import { vi } from 'vitest';
import {
  IncidentGateway,
  provideIncidentGateway,
} from '../../core/services/incident-gateway';
import { DashboardService } from './dashboard.service';

describe('DashboardService', () => {
  it('derives demo cards and recent incidents from the existing incident gateway', async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideIncidentGateway()],
    });

    const view = await firstValueFrom(
      TestBed.inject(DashboardService).overview(7)
    );

    expect(view.summary.activeIncidents).toBeGreaterThanOrEqual(0);
    expect(view.summary.criticalIncidents)
      .toBeLessThanOrEqual(view.summary.activeIncidents);
    expect(view.activeIncidents.every(row => row.status !== 'Resolved'))
      .toBe(true);
  });

  it('prioritizes unhealthy services and limits the dashboard to eight', async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideIncidentGateway()],
    });

    const gateway = TestBed.inject(IncidentGateway);
    const analytics = await firstValueFrom(gateway.analytics(7));
    const example = analytics.services[0];

    // Supply ten services to verify both priority order and the cap.
    const services = Array.from({ length: 10 }, (_, index) => ({
      ...example,
      service: `Service ${index.toString().padStart(2, '0')}`,
      health: (
        index === 9 ? 'Critical'
          : index === 8 ? 'At risk'
          : 'Healthy'
      ) as 'Critical' | 'At risk' | 'Healthy',
    }));

    vi.spyOn(gateway, 'analytics').mockReturnValue(
      of({ ...analytics, services })
    );

    const view = await firstValueFrom(
      TestBed.inject(DashboardService).overview(7)
    );

    expect(view.summary.servicesAtRisk).toBe(2);
    expect(view.services).toHaveLength(8);

    expect(view.services.map(service => service.service)).toEqual([
      'Service 09',
      'Service 08',
      'Service 00',
      'Service 01',
      'Service 02',
      'Service 03',
      'Service 04',
      'Service 05',
    ]);

    expect(view.services[0].trend).toEqual(example.trend);
  });
});