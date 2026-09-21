import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { DemoIncidentGateway, IncidentGateway } from './incident-gateway';
import { DEMO_INCIDENTS } from '../data/demo-incidents';

describe('DemoIncidentGateway', () => {
  let gateway: IncidentGateway;
  const filters = { query: '', severity: 'All' as const, status: 'All' as const, ownerTeam: '' };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [{ provide: IncidentGateway, useClass: DemoIncidentGateway }],
    });
    gateway = TestBed.inject(IncidentGateway);
  });

  it('returns independent copies of seeded incidents', async () => {
    const page = await firstValueFrom(gateway.list(filters, 1, 10));
    expect(page.items).toHaveLength(DEMO_INCIDENTS.length);
    expect(page.items[0].id).toBe('INC-1042');
  });

  it('adds a status event when the workflow advances', async () => {
    const updated = await firstValueFrom(gateway.updateStatus('INC-1042', 'Identified', 4));
    expect(updated.status).toBe('Identified');
    expect(updated.version).toBe(5);
    expect(updated.timeline[0].type).toBe('status');
  });

  it('creates a new incident in the investigating state', async () => {
    const created = await firstValueFrom(gateway.create({
      title: 'Synthetic monitor failure',
      summary: 'Probes are failing from two regions.',
      severity: 'SEV-2',
      service: 'Monitoring',
      ownerTeam: 'Reliability Engineering',
      assignee: 'Dan Fuhr',
    }));
    expect(created.id).toBe('INC-1043');
    expect(created.status).toBe('Investigating');
    expect(created.responders[0].role).toBe('Incident commander');
  });

  it('manages responders and tags with version checks', async () => {
    const responder = await firstValueFrom(gateway.addResponder(
      'INC-1042', 'Noah Williams', 'Subject matter expert', 4,
    ));
    expect(responder.responders.at(-1)?.name).toBe('Noah Williams');

    const tagged = await firstValueFrom(gateway.updateTags(
      'INC-1042', ['Payments', 'Customer-Impact'], responder.version,
    ));
    expect(tagged.tags).toEqual(['payments', 'customer-impact']);
  });
  it('executes structured commands and extracts mentions', async () => {
    const updated=await firstValueFrom(gateway.addTimelineEntry('INC-1042','/note Please check @Maya',4));
    expect(updated.timeline[0].command?.name).toBe('note'); expect(updated.timeline[0].mentions).toEqual(['Maya']);
  });

  it('correlates service health with reliability metrics', async () => {
    const overview = await firstValueFrom(gateway.analytics(30));
    expect(overview.windowDays).toBe(30);
    expect(overview.services).toHaveLength(4);
    expect(overview.services[0].trend).toHaveLength(7);
    expect(overview.metrics.customerImpactMinutes).toBeGreaterThan(0);
  });

  it('tracks postmortems and owned action items with versions', async () => {
    const postmortem = await firstValueFrom(gateway.savePostmortem('INC-1042', {
      owner: 'Maya Chen', status: 'InReview', executiveSummary: 'Summary', rootCause: 'Cause',
      detection: 'Alert', resolution: 'Rollback', lessonsLearned: 'Guard the retry budget', expectedVersion: null,
    }));
    const updated = await firstValueFrom(gateway.addActionItem('INC-1042', 'Add guardrail',
      'Noah Williams', '2026-09-30T17:00:00Z', postmortem.version));
    expect(updated.version).toBe(2);
    expect(updated.actionItems[0].owner).toBe('Noah Williams');
  });

  it('edits versioned service objectives', async () => {
    const objective=(await firstValueFrom(gateway.listObjectives()))[0];
    const updated=await firstValueFrom(gateway.updateObjective(objective.id,{
      ownerTeam:'Payments Reliability',availabilityTargetPercent:99.97,acknowledgementTargetMinutes:4,
      resolutionTargetMinutes:40,monthlyErrorBudgetMinutes:18,fastBurnThreshold:14,slowBurnThreshold:6,
      enabled:true,expectedVersion:objective.version,
    }));
    expect(updated.ownerTeam).toBe('Payments Reliability');expect(updated.version).toBe(2);
  });

  it('evaluates and acknowledges reliability signals', async () => {
    const evaluation=await firstValueFrom(gateway.evaluateReliability());
    const signal=evaluation.signals.find(item=>item.status==='Open')!;
    const acknowledged=await firstValueFrom(gateway.acknowledgeSignal(signal.id,signal.version));
    expect(acknowledged.status).toBe('Acknowledged');expect(acknowledged.acknowledgedBy).toBe('Dan Fuhr');
  });

  it('promotes a confirmed burn signal into incident response', async () => {
    const signal=(await firstValueFrom(gateway.listSignals())).find(item=>item.status==='Open')!;
    const promoted=await firstValueFrom(gateway.promoteSignal(signal.id,'Dan Fuhr',signal.version));
    expect(promoted.signal.status).toBe('Promoted');expect(promoted.incident.service).toBe('Payments API');
  });

  it('creates and removes maintenance windows', async () => {
    const created=await firstValueFrom(gateway.createMaintenanceWindow('Payments API','Database failover',
      '2026-09-21T10:00:00Z','2026-09-21T11:00:00Z'));
    expect((await firstValueFrom(gateway.listMaintenanceWindows())).some(item=>item.id===created.id)).toBe(true);
    await firstValueFrom(gateway.deleteMaintenanceWindow(created.id,created.version));
    expect((await firstValueFrom(gateway.listMaintenanceWindows())).some(item=>item.id===created.id)).toBe(false);
  });
});
