import { EnvironmentProviders, Injectable, makeEnvironmentProviders } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { BehaviorSubject, Observable, catchError, delay, map, of, take, throwError } from 'rxjs';
import { ActionItemStatus, AnalyticsOverview, CreateIncidentRequest, Incident, IncidentFilters, IncidentPage, IncidentStatus, MaintenanceWindow, Postmortem, ReliabilityEvaluation, ReliabilitySignal, SavePostmortemRequest, ServiceObjective, SignalPromotion, UpdateServiceObjectiveRequest } from '../models/incident';
import { DEMO_INCIDENTS } from '../data/demo-incidents';
import { environment } from '../../../environments/environment';

/** Components depend on this contract, never on a particular transport. */
export abstract class IncidentGateway {
  abstract list(filters: IncidentFilters, page: number, pageSize: number): Observable<IncidentPage>;
  abstract get(id: string): Observable<Incident>;
  abstract create(request: CreateIncidentRequest): Observable<Incident>;
  abstract updateStatus(id: string, status: IncidentStatus, expectedVersion: number): Observable<Incident>;
  abstract addNote(id: string, note: string, expectedVersion: number): Observable<Incident>;
  abstract addTimelineEntry(id: string, message: string, expectedVersion: number): Observable<Incident>;
  abstract updateAssignment(id: string, assignee: string, expectedVersion: number): Observable<Incident>;
  abstract addResponder(id: string, name: string, role: string, expectedVersion: number): Observable<Incident>;
  abstract removeResponder(id: string, responderId: string, expectedVersion: number): Observable<Incident>;
  abstract updateTags(id: string, tags: string[], expectedVersion: number): Observable<Incident>;
  abstract analytics(days: number): Observable<AnalyticsOverview>;
  abstract getPostmortem(id: string): Observable<Postmortem | null>;
  abstract savePostmortem(id: string, request: SavePostmortemRequest): Observable<Postmortem>;
  abstract addActionItem(id: string, title: string, owner: string, dueAt: string, expectedVersion: number): Observable<Postmortem>;
  abstract updateActionItem(id: string, actionId: string, status: ActionItemStatus, owner: string, dueAt: string, expectedVersion: number): Observable<Postmortem>;
  abstract exportEvidence(id: string, format: 'json' | 'csv'): Observable<Blob>;
  abstract listObjectives(): Observable<ServiceObjective[]>;
  abstract updateObjective(id: string, request: UpdateServiceObjectiveRequest): Observable<ServiceObjective>;
  abstract listSignals(): Observable<ReliabilitySignal[]>;
  abstract evaluateReliability(): Observable<ReliabilityEvaluation>;
  abstract acknowledgeSignal(id: string, expectedVersion: number): Observable<ReliabilitySignal>;
  abstract promoteSignal(id: string, assignee: string, expectedVersion: number): Observable<SignalPromotion>;
  abstract listMaintenanceWindows(): Observable<MaintenanceWindow[]>;
  abstract createMaintenanceWindow(service: string, title: string, startsAt: string, endsAt: string): Observable<MaintenanceWindow>;
  abstract deleteMaintenanceWindow(id: string, expectedVersion: number): Observable<void>;
}

export class IncidentVersionConflictError extends Error {
  constructor(readonly current: Incident) {
    super('The incident changed after it was loaded.');
  }
}

@Injectable()
export class DemoIncidentGateway extends IncidentGateway {
  private readonly incidents = new BehaviorSubject<Incident[]>(structuredClone([...DEMO_INCIDENTS]));
  private readonly postmortems = new Map<string, Postmortem>();
  private readonly objectives = new BehaviorSubject<ServiceObjective[]>([
    {id:'obj-payments',version:1,service:'Payments API',ownerTeam:'Payments Platform',availabilityTargetPercent:99.95,
      acknowledgementTargetMinutes:5,resolutionTargetMinutes:45,monthlyErrorBudgetMinutes:22,fastBurnThreshold:14,slowBurnThreshold:6,enabled:true},
    {id:'obj-search',version:1,service:'Catalog Search',ownerTeam:'Discovery Engineering',availabilityTargetPercent:99.9,
      acknowledgementTargetMinutes:10,resolutionTargetMinutes:90,monthlyErrorBudgetMinutes:43,fastBurnThreshold:14,slowBurnThreshold:6,enabled:true},
    {id:'obj-events',version:1,service:'Event Delivery',ownerTeam:'Integration Platform',availabilityTargetPercent:99.9,
      acknowledgementTargetMinutes:10,resolutionTargetMinutes:60,monthlyErrorBudgetMinutes:43,fastBurnThreshold:14,slowBurnThreshold:6,enabled:true},
    {id:'obj-edge',version:1,service:'Edge Gateway',ownerTeam:'Traffic Engineering',availabilityTargetPercent:99.99,
      acknowledgementTargetMinutes:3,resolutionTargetMinutes:30,monthlyErrorBudgetMinutes:4,fastBurnThreshold:14,slowBurnThreshold:6,enabled:true},
  ]);
  private readonly signals = new BehaviorSubject<ReliabilitySignal[]>([
    {id:'sig-payments',version:1,service:'Payments API',status:'Open',suggestedSeverity:'SEV-1',evaluationWindowMinutes:60,
      burnRate:16,observedAvailabilityPercent:99.2,targetAvailabilityPercent:99.95,summary:'60-minute SLO burn is 16.00x for Payments API.',
      firstObservedAt:'2026-09-20T15:30:00Z',lastObservedAt:'2026-09-20T15:55:00Z'},
    {id:'sig-events',version:1,service:'Event Delivery',status:'Suppressed',suggestedSeverity:'SEV-2',evaluationWindowMinutes:60,
      burnRate:17,observedAvailabilityPercent:98.3,targetAvailabilityPercent:99.9,summary:'60-minute SLO burn is 17.00x for Event Delivery.',
      firstObservedAt:'2026-09-20T15:30:00Z',lastObservedAt:'2026-09-20T15:55:00Z',suppressionReason:'Maintenance: Partner retry-policy rollout'},
  ]);
  private readonly maintenanceWindows = new BehaviorSubject<MaintenanceWindow[]>([
    {id:'mw-events',version:1,service:'Event Delivery',title:'Partner retry-policy rollout',startsAt:'2026-09-20T15:00:00Z',
      endsAt:'2026-09-20T18:00:00Z',createdBy:'Elena Ortiz',createdAt:'2026-09-20T14:30:00Z',active:true},
  ]);

  override list(filters: IncidentFilters, page: number, pageSize: number): Observable<IncidentPage> {
    return this.incidents.asObservable().pipe(
      take(1),
      map((incidents) => { const q = filters.query.trim().toLowerCase(); const filtered = incidents.filter((i) =>
        (!q || [i.id,i.title,i.service,i.ownerTeam,i.assignee,...i.tags].some(v => v.toLowerCase().includes(q))) &&
        (filters.severity === 'All' || i.severity === filters.severity) && (filters.status === 'All' || i.status === filters.status) &&
        (!filters.ownerTeam || i.ownerTeam.toLowerCase().includes(filters.ownerTeam.toLowerCase())));
        const start = (page - 1) * pageSize; return { items: structuredClone(filtered.slice(start,start+pageSize)), total: filtered.length,
          page, pageSize, totalPages: filtered.length ? Math.ceil(filtered.length/pageSize) : 0 }; }),
      delay(180),
    );
  }
  override get(id: string): Observable<Incident> { const item = this.incidents.value.find(i => i.id === id);
    return item ? of(structuredClone(item)).pipe(delay(80)) : throwError(() => new Error(`Incident ${id} was not found.`)); }

  override create(request: CreateIncidentRequest): Observable<Incident> {
    const now = new Date().toISOString();
    const sequence = 1043 + this.incidents.value.length - DEMO_INCIDENTS.length;
    const incident: Incident = {
      ...request,
      id: `INC-${sequence}`,
      version: 1,
      status: 'Investigating',
      declaredAt: now,
      updatedAt: now,
      affectedCustomers: 0,
      tags: [],
      responders: [{ id: crypto.randomUUID(), name: request.assignee, role: 'Incident commander', joinedAt: now }],
      timeline: [{ id: crypto.randomUUID(), occurredAt: now, actor: 'Dan Fuhr', type: 'declared', message: 'Incident declared from the IncidentLens console.' }],
    };
    this.incidents.next([incident, ...this.incidents.value]);
    return of(structuredClone(incident)).pipe(delay(150));
  }

  override updateStatus(id: string, status: IncidentStatus, expectedVersion: number): Observable<Incident> {
    return this.update(id, expectedVersion, (incident, now) => ({
      ...incident,
      status,
      acknowledgedAt: incident.acknowledgedAt ?? (status !== 'Investigating' ? now : null),
      resolvedAt: status === 'Resolved' ? (incident.resolvedAt ?? now) : null,
      timeline: [this.event(now, 'status', `Status changed to ${status}.`), ...incident.timeline],
    }));
  }

  override addNote(id: string, note: string, expectedVersion: number): Observable<Incident> {
    return this.update(id, expectedVersion, (incident, now) => ({
      ...incident,
      timeline: [this.event(now, 'note', note.trim()), ...incident.timeline],
    }));
  }
  override addTimelineEntry(id: string, message: string, expectedVersion: number): Observable<Incident> {
    return this.update(id, expectedVersion, (incident, now) => { const text = message.trim();
      const mentions = [...text.matchAll(/(?:^|\s)@([A-Za-z0-9._-]+)/g)].map(m => m[1]); const command = text.match(/^\/(status|assign|note)\s+(.+)$/i);
      let updated = incident; if (command?.[1].toLowerCase() === 'status') { const status = this.parseStatus(command[2]); if (status) updated = {...incident,status}; }
      else if (command?.[1].toLowerCase() === 'assign') updated = {...incident,assignee:command[2].trim()};
      return {...updated,timeline:[{...this.event(now,command?'command':'note',text), command: command?{name:command[1].toLowerCase(),arguments:command[2]}:null,mentions},...incident.timeline]}; });
  }

  override updateAssignment(id: string, assignee: string, expectedVersion: number): Observable<Incident> {
    return this.update(id, expectedVersion, (incident, now) => ({
      ...incident,
      assignee: assignee.trim(),
      timeline: [this.event(now, 'assignment', `Incident command transferred from ${incident.assignee} to ${assignee.trim()}.`), ...incident.timeline],
    }));
  }

  override addResponder(id: string, name: string, role: string, expectedVersion: number): Observable<Incident> {
    return this.update(id, expectedVersion, (incident, now) => ({
      ...incident,
      responders: [...incident.responders, { id: crypto.randomUUID(), name: name.trim(), role: role.trim(), joinedAt: now }],
      timeline: [this.event(now, 'responder', `${name.trim()} joined as ${role.trim()}.`), ...incident.timeline],
    }));
  }

  override removeResponder(id: string, responderId: string, expectedVersion: number): Observable<Incident> {
    return this.update(id, expectedVersion, (incident, now) => {
      const responder = incident.responders.find((item) => item.id === responderId);
      return {
        ...incident,
        responders: incident.responders.filter((item) => item.id !== responderId),
        timeline: responder
          ? [this.event(now, 'responder', `${responder.name} left the response team.`), ...incident.timeline]
          : incident.timeline,
      };
    });
  }

  override updateTags(id: string, tags: string[], expectedVersion: number): Observable<Incident> {
    const normalized = [...new Set(tags.map((tag) => tag.trim().toLowerCase()).filter(Boolean))];
    return this.update(id, expectedVersion, (incident, now) => ({
      ...incident,
      tags: normalized,
      timeline: [this.event(now, 'tags', `Incident tags updated: ${normalized.join(', ') || 'none'}.`), ...incident.timeline],
    }));
  }

  override analytics(days: number): Observable<AnalyticsOverview> {
    const services = [
      ['Payments API', 99.95, 99.93, 82, 'At risk'],
      ['Catalog Search', 99.9, 99.96, 31, 'Healthy'],
      ['Event Delivery', 99.9, 99.89, 104, 'Critical'],
      ['Edge Gateway', 99.99, 99.995, 18, 'Healthy'],
    ] as const;
    const overview: AnalyticsOverview = {
      generatedAt: new Date().toISOString(), windowDays: days,
      metrics: { meanTimeToAcknowledgeMinutes: 6.4, meanTimeToResolveMinutes: 47.5,
        recurrenceRatePercent: 20, customerImpactMinutes: 128340, resolvedIncidents: 2, activeIncidents: 3 },
      services: services.map(([service,target,current,budget,health], serviceIndex) => ({ service,
        availabilityTargetPercent: target, currentAvailabilityPercent: current,
        errorBudgetConsumedPercent: budget, incidentCount: service === 'Payments API' ? 2 : 1,
        sloBreaches: budget >= 75 ? 1 : 0, affectedCustomers: service === 'Payments API' ? 2272 : 624,
        health: health as 'Healthy' | 'At risk' | 'Critical', trend: Array.from({length: 7}, (_,index) => ({
          capturedAt: new Date(Date.UTC(2026,8,12+index)).toISOString(),
          availabilityPercent: Math.min(100,current - .02 + ((index + serviceIndex) % 3) * .01),
          errorRatePercent: Math.max(0,.2 - index * .015), latencyP95Milliseconds: 180 + serviceIndex * 130 + index * 8,
        })) })),
    };
    return of(overview).pipe(delay(120));
  }

  override getPostmortem(id: string): Observable<Postmortem | null> {
    return of(structuredClone(this.postmortems.get(id) ?? null)).pipe(delay(80));
  }

  override savePostmortem(id: string, request: SavePostmortemRequest): Observable<Postmortem> {
    const existing = this.postmortems.get(id);
    if (existing && existing.version !== request.expectedVersion)
      return throwError(() => new Error('The postmortem changed after it was loaded.'));
    const now = new Date().toISOString();
    const saved: Postmortem = { id: existing?.id ?? crypto.randomUUID(), incidentId: id,
      version: (existing?.version ?? 0) + 1, status: request.status, owner: request.owner,
      executiveSummary: request.executiveSummary, rootCause: request.rootCause, detection: request.detection,
      resolution: request.resolution, lessonsLearned: request.lessonsLearned,
      createdAt: existing?.createdAt ?? now, updatedAt: now, actionItems: existing?.actionItems ?? [] };
    this.postmortems.set(id, saved); return of(structuredClone(saved)).pipe(delay(120));
  }

  override addActionItem(id: string, title: string, owner: string, dueAt: string, expectedVersion: number): Observable<Postmortem> {
    const existing = this.postmortems.get(id);
    if (!existing || existing.version !== expectedVersion) return throwError(() => new Error('The postmortem changed.'));
    const now = new Date().toISOString();
    const updated = { ...existing, version: existing.version + 1, updatedAt: now,
      actionItems: [...existing.actionItems, { id: crypto.randomUUID(), title, owner, dueAt,
        status: 'Open' as const, createdAt: now, completedAt: null }] };
    this.postmortems.set(id, updated); return of(structuredClone(updated)).pipe(delay(100));
  }

  override updateActionItem(id: string, actionId: string, status: ActionItemStatus, owner: string, dueAt: string, expectedVersion: number): Observable<Postmortem> {
    const existing = this.postmortems.get(id);
    if (!existing || existing.version !== expectedVersion) return throwError(() => new Error('The postmortem changed.'));
    const now = new Date().toISOString(); const updated = { ...existing, version: existing.version + 1, updatedAt: now,
      actionItems: existing.actionItems.map(item => item.id === actionId ? {...item,status,owner,dueAt,completedAt:status==='Done'?now:null}:item) };
    this.postmortems.set(id, updated); return of(structuredClone(updated)).pipe(delay(100));
  }

  override exportEvidence(id: string, format: 'json' | 'csv'): Observable<Blob> {
    const incident = this.incidents.value.find(item => item.id === id); const postmortem = this.postmortems.get(id) ?? null;
    const body = format === 'json' ? JSON.stringify({generatedAt:new Date().toISOString(),incident,postmortem},null,2)
      : `recordType,incidentId,title\nincident,${id},"${incident?.title ?? ''}"`;
    return of(new Blob([body], {type: format === 'json' ? 'application/json' : 'text/csv'}));
  }

  override listObjectives() { return of(structuredClone(this.objectives.value)).pipe(delay(80)); }
  override updateObjective(id: string, request: UpdateServiceObjectiveRequest) { const current=this.objectives.value.find(item=>item.id===id);
    if(!current||current.version!==request.expectedVersion)return throwError(()=>new Error('The objective changed.'));
    const updated={...current,...request,version:current.version+1};this.objectives.next(this.objectives.value.map(item=>item.id===id?updated:item));return of(structuredClone(updated)).pipe(delay(100)); }
  override listSignals() { return of(structuredClone(this.signals.value)).pipe(delay(80)); }
  override evaluateReliability() { const now=new Date().toISOString();this.signals.next(this.signals.value.map(item=>({...item,lastObservedAt:now,version:item.version+1})));
    return of({evaluatedAt:now,servicesEvaluated:this.objectives.value.length,signalsCreated:0,signalsUpdated:this.signals.value.length,
      signalsSuppressed:this.signals.value.filter(item=>item.status==='Suppressed').length,signals:structuredClone(this.signals.value)}).pipe(delay(120)); }
  override acknowledgeSignal(id:string,expectedVersion:number){const current=this.signals.value.find(item=>item.id===id);if(!current||current.version!==expectedVersion)return throwError(()=>new Error('The signal changed.'));
    const updated={...current,status:'Acknowledged' as const,version:current.version+1,acknowledgedAt:new Date().toISOString(),acknowledgedBy:'Dan Fuhr'};
    this.signals.next(this.signals.value.map(item=>item.id===id?updated:item));return of(structuredClone(updated)).pipe(delay(90));}
  override promoteSignal(id:string,assignee:string,expectedVersion:number){const current=this.signals.value.find(item=>item.id===id);if(!current||current.version!==expectedVersion||current.status==='Suppressed')return throwError(()=>new Error('The signal cannot be promoted.'));
    const incidentRequest={title:`SLO burn detected for ${current.service}`,summary:current.summary,severity:current.suggestedSeverity,service:current.service,
      ownerTeam:this.objectives.value.find(item=>item.service===current.service)?.ownerTeam??'Reliability Engineering',assignee};
    return this.create(incidentRequest).pipe(map(incident=>{const updated={...current,status:'Promoted' as const,version:current.version+1,incidentId:incident.id};
      this.signals.next(this.signals.value.map(item=>item.id===id?updated:item));return {signal:structuredClone(updated),incident};}));}
  override listMaintenanceWindows(){return of(structuredClone(this.maintenanceWindows.value)).pipe(delay(80));}
  override createMaintenanceWindow(service:string,title:string,startsAt:string,endsAt:string){const item={id:crypto.randomUUID(),version:1,service,title,startsAt,endsAt,
    createdBy:'Dan Fuhr',createdAt:new Date().toISOString(),active:new Date(startsAt)<=new Date()&&new Date(endsAt)>=new Date()};this.maintenanceWindows.next([item,...this.maintenanceWindows.value]);return of(structuredClone(item)).pipe(delay(100));}
  override deleteMaintenanceWindow(id:string,expectedVersion:number){const current=this.maintenanceWindows.value.find(item=>item.id===id);if(!current||current.version!==expectedVersion)return throwError(()=>new Error('The window changed.'));
    this.maintenanceWindows.next(this.maintenanceWindows.value.filter(item=>item.id!==id));return of(void 0).pipe(delay(80));}

  private update(
    id: string,
    expectedVersion: number,
    updater: (incident: Incident, now: string) => Incident,
  ): Observable<Incident> {
    const existing = this.incidents.value.find((incident) => incident.id === id);
    if (!existing) return throwError(() => new Error(`Incident ${id} was not found.`));
    if (existing.version !== expectedVersion) {
      return throwError(() => new IncidentVersionConflictError(structuredClone(existing)));
    }

    const now = new Date().toISOString();
    const updated = { ...updater(existing, now), version: existing.version + 1, updatedAt: now };
    this.incidents.next(this.incidents.value.map((incident) => incident.id === id ? updated : incident));
    return of(structuredClone(updated)).pipe(delay(120));
  }

  private event(now: string, type: Incident['timeline'][number]['type'], message: string) {
    return { id: crypto.randomUUID(), occurredAt: now, actor: 'Dan Fuhr', type, message };
  }
  private parseStatus(value: string): IncidentStatus | null { const s=value.trim().toLowerCase(); return ['investigating','identified','monitoring','resolved'].includes(s)
    ? `${s[0].toUpperCase()}${s.slice(1)}` as IncidentStatus : null; }
}

@Injectable()
export class HttpIncidentGateway extends IncidentGateway {
  constructor(private readonly http: HttpClient) { super(); }

  override list(filters: IncidentFilters, page: number, pageSize: number) { let params = new HttpParams().set('page',page).set('pageSize',pageSize);
    if(filters.query.trim()) params=params.set('query',filters.query.trim()); if(filters.severity!=='All') params=params.set('severity',filters.severity);
    if(filters.status!=='All') params=params.set('status',filters.status); if(filters.ownerTeam.trim()) params=params.set('ownerTeam',filters.ownerTeam.trim());
    return this.http.get<IncidentPage>(`${environment.apiBaseUrl}/incidents`,{params}); }
  override get(id: string) { return this.http.get<Incident>(`${environment.apiBaseUrl}/incidents/${id}`); }
  override create(request: CreateIncidentRequest) { return this.http.post<Incident>(`${environment.apiBaseUrl}/incidents`, request); }
  override updateStatus(id: string, status: IncidentStatus, expectedVersion: number) {
    return this.http.patch<Incident>(`${environment.apiBaseUrl}/incidents/${id}/status`, { status, expectedVersion });
  }
  override addNote(id: string, note: string, expectedVersion: number) {
    return this.http.post<Incident>(`${environment.apiBaseUrl}/incidents/${id}/notes`, { note, expectedVersion });
  }
  override addTimelineEntry(id: string, message: string, expectedVersion: number) { return this.http.post<Incident>(`${environment.apiBaseUrl}/incidents/${id}/timeline`,{message,expectedVersion}); }
  override updateAssignment(id: string, assignee: string, expectedVersion: number) {
    return this.http.patch<Incident>(`${environment.apiBaseUrl}/incidents/${id}/assignment`, { assignee, expectedVersion });
  }
  override addResponder(id: string, name: string, role: string, expectedVersion: number) {
    return this.http.post<Incident>(`${environment.apiBaseUrl}/incidents/${id}/responders`, { name, role, expectedVersion });
  }
  override removeResponder(id: string, responderId: string, expectedVersion: number) {
    return this.http.delete<Incident>(`${environment.apiBaseUrl}/incidents/${id}/responders/${responderId}`, { params: { expectedVersion } });
  }
  override updateTags(id: string, tags: string[], expectedVersion: number) {
    return this.http.put<Incident>(`${environment.apiBaseUrl}/incidents/${id}/tags`, { tags, expectedVersion });
  }
  override analytics(days: number) { return this.http.get<AnalyticsOverview>(`${environment.apiBaseUrl}/analytics/overview`, { params: { days } }); }
  override getPostmortem(id: string) { return this.http.get<Postmortem>(`${environment.apiBaseUrl}/incidents/${id}/postmortem`).pipe(
    catchError(error => error.status === 404 ? of(null) : throwError(() => error))); }
  override savePostmortem(id: string, request: SavePostmortemRequest) { return this.http.put<Postmortem>(`${environment.apiBaseUrl}/incidents/${id}/postmortem`, request); }
  override addActionItem(id: string, title: string, owner: string, dueAt: string, expectedPostmortemVersion: number) {
    return this.http.post<Postmortem>(`${environment.apiBaseUrl}/incidents/${id}/postmortem/actions`, {title,owner,dueAt,expectedPostmortemVersion}); }
  override updateActionItem(id: string, actionId: string, status: ActionItemStatus, owner: string, dueAt: string, expectedPostmortemVersion: number) {
    return this.http.patch<Postmortem>(`${environment.apiBaseUrl}/incidents/${id}/postmortem/actions/${actionId}`, {status,owner,dueAt,expectedPostmortemVersion}); }
  override exportEvidence(id: string, format: 'json' | 'csv') { return this.http.get(`${environment.apiBaseUrl}/incidents/${id}/export`, {params:{format},responseType:'blob'}); }
  override listObjectives(){return this.http.get<ServiceObjective[]>(`${environment.apiBaseUrl}/reliability/objectives`);}
  override updateObjective(id:string,request:UpdateServiceObjectiveRequest){return this.http.put<ServiceObjective>(`${environment.apiBaseUrl}/reliability/objectives/${id}`,request);}
  override listSignals(){return this.http.get<ReliabilitySignal[]>(`${environment.apiBaseUrl}/reliability/signals`);}
  override evaluateReliability(){return this.http.post<ReliabilityEvaluation>(`${environment.apiBaseUrl}/reliability/evaluate`,{});}
  override acknowledgeSignal(id:string,expectedVersion:number){return this.http.patch<ReliabilitySignal>(`${environment.apiBaseUrl}/reliability/signals/${id}/acknowledge`,{expectedVersion});}
  override promoteSignal(id:string,assignee:string,expectedVersion:number){return this.http.post<SignalPromotion>(`${environment.apiBaseUrl}/reliability/signals/${id}/promote`,{assignee,expectedVersion});}
  override listMaintenanceWindows(){return this.http.get<MaintenanceWindow[]>(`${environment.apiBaseUrl}/reliability/maintenance-windows`);}
  override createMaintenanceWindow(service:string,title:string,startsAt:string,endsAt:string){return this.http.post<MaintenanceWindow>(`${environment.apiBaseUrl}/reliability/maintenance-windows`,{service,title,startsAt,endsAt});}
  override deleteMaintenanceWindow(id:string,expectedVersion:number){return this.http.delete<void>(`${environment.apiBaseUrl}/reliability/maintenance-windows/${id}`,{params:{expectedVersion}});}
}

export function provideIncidentGateway(): EnvironmentProviders {
  return makeEnvironmentProviders([{
    provide: IncidentGateway,
    useClass: environment.demoMode ? DemoIncidentGateway : HttpIncidentGateway,
  }]);
}
