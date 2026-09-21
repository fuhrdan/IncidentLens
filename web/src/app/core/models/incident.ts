/** Severity reflects customer and business impact, not remediation difficulty. */
export type Severity = 'SEV-1' | 'SEV-2' | 'SEV-3' | 'SEV-4';

/** The small state machine used by the IncidentLens incident workflow. */
export type IncidentStatus = 'Investigating' | 'Identified' | 'Monitoring' | 'Resolved';

export type TimelineEventType = 'declared' | 'status' | 'note' | 'assignment' | 'responder' | 'tags' | 'command' | 'postmortem';
export interface TimelineCommand { name: string; arguments: string; }

export interface TimelineEvent {
  id: string;
  occurredAt: string;
  actor: string;
  type: TimelineEventType;
  message: string;
  command?: TimelineCommand | null;
  mentions?: string[];
}

export interface IncidentResponder {
  id: string;
  name: string;
  role: string;
  joinedAt: string;
}

export interface Incident {
  id: string;
  version: number;
  title: string;
  summary: string;
  severity: Severity;
  status: IncidentStatus;
  service: string;
  ownerTeam: string;
  assignee: string;
  declaredAt: string;
  updatedAt: string;
  acknowledgedAt?: string | null;
  resolvedAt?: string | null;
  affectedCustomers: number;
  responders: IncidentResponder[];
  tags: string[];
  timeline: TimelineEvent[];
}

export interface CreateIncidentRequest {
  title: string;
  summary: string;
  severity: Severity;
  service: string;
  ownerTeam: string;
  assignee: string;
}

export interface ConflictResponse {
  code: 'version_conflict';
  message: string;
  current: Incident;
}

export interface IncidentFilters {
  query: string;
  severity: Severity | 'All';
  status: IncidentStatus | 'All';
  ownerTeam: string;
}
export interface IncidentPage { items: Incident[]; total: number; page: number; pageSize: number; totalPages: number; }
export interface PresenceUser { connectionId: string; name: string; role: 'Commander' | 'Viewer'; joinedAt: string; }
export interface IncidentChangedEvent { incidentId: string; version: number; kind: string; occurredAt: string; }

export interface ReliabilityMetrics {
  meanTimeToAcknowledgeMinutes: number;
  meanTimeToResolveMinutes: number;
  recurrenceRatePercent: number;
  customerImpactMinutes: number;
  resolvedIncidents: number;
  activeIncidents: number;
}
export interface ServiceTrendPoint { capturedAt: string; availabilityPercent: number; errorRatePercent: number; latencyP95Milliseconds: number; }
export interface ServiceHealth {
  service: string;
  availabilityTargetPercent: number;
  currentAvailabilityPercent: number;
  errorBudgetConsumedPercent: number;
  incidentCount: number;
  sloBreaches: number;
  affectedCustomers: number;
  health: 'Healthy' | 'At risk' | 'Critical';
  trend: ServiceTrendPoint[];
}
export interface AnalyticsOverview { generatedAt: string; windowDays: number; metrics: ReliabilityMetrics; services: ServiceHealth[]; }

export type PostmortemStatus = 'Draft' | 'InReview' | 'Published';
export type ActionItemStatus = 'Open' | 'InProgress' | 'Done';
export interface PostmortemActionItem {
  id: string; title: string; owner: string; status: ActionItemStatus;
  dueAt: string; createdAt: string; completedAt?: string | null;
}
export interface Postmortem {
  id: string; incidentId: string; version: number; status: PostmortemStatus; owner: string;
  executiveSummary: string; rootCause: string; detection: string; resolution: string;
  lessonsLearned: string; createdAt: string; updatedAt: string; actionItems: PostmortemActionItem[];
}
export interface SavePostmortemRequest {
  owner: string; status: PostmortemStatus; executiveSummary: string; rootCause: string;
  detection: string; resolution: string; lessonsLearned: string; expectedVersion: number | null;
}

export interface ServiceObjective {
  id: string; version: number; service: string; ownerTeam: string;
  availabilityTargetPercent: number; acknowledgementTargetMinutes: number;
  resolutionTargetMinutes: number; monthlyErrorBudgetMinutes: number;
  fastBurnThreshold: number; slowBurnThreshold: number; enabled: boolean;
}
export interface UpdateServiceObjectiveRequest extends Omit<ServiceObjective, 'id' | 'service' | 'version'> {
  expectedVersion: number;
}
export interface MaintenanceWindow {
  id: string; version: number; service: string; title: string; startsAt: string; endsAt: string;
  createdBy: string; createdAt: string; active: boolean;
}
export type ReliabilitySignalStatus = 'Open' | 'Acknowledged' | 'Promoted' | 'Suppressed';
export interface ReliabilitySignal {
  id: string; version: number; service: string; status: ReliabilitySignalStatus;
  suggestedSeverity: Severity; evaluationWindowMinutes: number; burnRate: number;
  observedAvailabilityPercent: number; targetAvailabilityPercent: number; summary: string;
  firstObservedAt: string; lastObservedAt: string; acknowledgedAt?: string | null;
  acknowledgedBy?: string | null; incidentId?: string | null; suppressionReason?: string | null;
}
export interface ReliabilityEvaluation {
  evaluatedAt: string; servicesEvaluated: number; signalsCreated: number;
  signalsUpdated: number; signalsSuppressed: number; signals: ReliabilitySignal[];
}
export interface SignalPromotion { signal: ReliabilitySignal; incident: Incident; }

export interface IncidentMetrics {
  active: number;
  critical: number;
  monitoring: number;
  meanTimeToAcknowledgeMinutes: number;
}

export const INCIDENT_STATUSES: readonly IncidentStatus[] = [
  'Investigating',
  'Identified',
  'Monitoring',
  'Resolved',
];

export const SEVERITIES: readonly Severity[] = ['SEV-1', 'SEV-2', 'SEV-3', 'SEV-4'];
