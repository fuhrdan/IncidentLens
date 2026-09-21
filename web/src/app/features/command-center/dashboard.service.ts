import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, forkJoin, map } from 'rxjs';
import { environment } from '../../../environments/environment';
import { IncidentGateway } from '../../core/services/incident-gateway';
import { DashboardOverview } from './dashboard.models';

/** Live API data in connected mode; derives the same view from the existing demo gateway otherwise. */
@Injectable({ providedIn: 'root' })
export class DashboardService {
  private readonly http = inject(HttpClient);
  private readonly gateway = inject(IncidentGateway);

  overview(days = 7): Observable<DashboardOverview> {
    if (!environment.demoMode) {
      return this.http.get<DashboardOverview>(`${environment.apiBaseUrl}/dashboard/overview`, {
        params: new HttpParams().set('days', days),
      });
    }

    return forkJoin({
      page: this.gateway.list({ query: '', severity: 'All', status: 'All', ownerTeam: '' }, 1, 100),
      analytics: this.gateway.analytics(days),
    }).pipe(map(({ page, analytics }) => {
      const open = page.items.filter(incident => incident.status !== 'Resolved');
      return {
        generatedAt: new Date().toISOString(),
        windowDays: days,
        summary: {
          activeIncidents: open.length,
          criticalIncidents: open.filter(incident => incident.severity === 'SEV-1').length,
          mttaMinutes: analytics.metrics.meanTimeToAcknowledgeMinutes,
          mttrMinutes: analytics.metrics.resolvedIncidents ? analytics.metrics.meanTimeToResolveMinutes : null,
          servicesAtRisk: analytics.services.filter(service => service.health !== 'Healthy').length,
        },
        activeIncidents: open.slice().sort((a, b) => b.declaredAt.localeCompare(a.declaredAt))
          .slice(0, 12).map(incident => ({
            id: incident.id, title: incident.title, severity: incident.severity,
            status: incident.status, ownerTeam: incident.ownerTeam, assignee: incident.assignee,
            service: incident.service, responderCount: incident.responders.length,
            declaredAt: incident.declaredAt,
          })),
      } satisfies DashboardOverview;
    }));
  }
}
