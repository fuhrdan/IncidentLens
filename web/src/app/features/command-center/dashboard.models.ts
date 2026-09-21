/** Matches the read-only, tenant-scoped /api/dashboard/overview payload. */
export interface DashboardSummary {
  activeIncidents: number;
  criticalIncidents: number;
  mttaMinutes: number | null;
  mttrMinutes: number | null;
  servicesAtRisk: number;
}

export interface DashboardIncident {
  id: string;
  title: string;
  severity: 'SEV-1' | 'SEV-2' | 'SEV-3' | 'SEV-4';
  status: 'Investigating' | 'Identified' | 'Monitoring' | 'Resolved';
  ownerTeam: string;
  assignee: string;
  service: string;
  responderCount: number;
  declaredAt: string;
}

export interface DashboardOverview {
  generatedAt: string;
  windowDays: number;
  summary: DashboardSummary;
  activeIncidents: DashboardIncident[];
}
