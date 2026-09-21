import { ChangeDetectionStrategy, Component, ElementRef, ViewChild, effect, inject } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActionItemStatus, INCIDENT_STATUSES, IncidentStatus, PostmortemStatus, SEVERITIES, ServiceObjective } from '../../core/models/incident';
import { IncidentStore } from '../../core/services/incident-store';
import { AuthService } from '../../core/services/auth.service';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'app-dashboard',
  imports: [DatePipe, DecimalPipe, ReactiveFormsModule],
  providers: [IncidentStore],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Dashboard {
  @ViewChild('incidentDialog') private dialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('objectiveDialog') private objectiveDialog?: ElementRef<HTMLDialogElement>;

  private readonly formBuilder = inject(FormBuilder);
  readonly store = inject(IncidentStore);
  readonly auth = inject(AuthService);
  readonly demoMode = environment.demoMode;
  readonly severities = SEVERITIES;
  readonly statuses = INCIDENT_STATUSES;
  editingObjective:ServiceObjective|null=null;

  readonly filters = this.formBuilder.nonNullable.group({
    query: [''],
    severity: ['All' as const],
    status: ['All' as const],
    ownerTeam: [''],
  });

  readonly createForm = this.formBuilder.nonNullable.group({
    title: ['', [Validators.required, Validators.maxLength(120)]],
    summary: ['', [Validators.required, Validators.maxLength(500)]],
    severity: ['SEV-2' as const, Validators.required],
    service: ['', [Validators.required, Validators.maxLength(80)]],
    ownerTeam: ['', [Validators.required, Validators.maxLength(80)]],
    assignee: ['', [Validators.required, Validators.maxLength(80)]],
  });

  readonly noteForm = this.formBuilder.nonNullable.group({
    note: ['', [Validators.required, Validators.maxLength(500)]],
  });

  readonly assignmentForm = this.formBuilder.nonNullable.group({
    assignee: ['', [Validators.required, Validators.maxLength(80)]],
  });

  readonly responderForm = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(80)]],
    role: ['', [Validators.required, Validators.maxLength(60)]],
  });

  readonly tagsForm = this.formBuilder.nonNullable.group({
    tags: ['', Validators.maxLength(260)],
  });

  readonly postmortemForm = this.formBuilder.nonNullable.group({
    owner: ['', [Validators.required, Validators.maxLength(80)]],
    status: ['Draft' as PostmortemStatus, Validators.required],
    executiveSummary: ['', [Validators.required, Validators.maxLength(1000)]],
    rootCause: ['', [Validators.required, Validators.maxLength(2000)]],
    detection: ['', [Validators.required, Validators.maxLength(1000)]],
    resolution: ['', [Validators.required, Validators.maxLength(2000)]],
    lessonsLearned: ['', [Validators.required, Validators.maxLength(2000)]],
  });

  readonly actionForm = this.formBuilder.nonNullable.group({
    title: ['', [Validators.required, Validators.maxLength(240)]],
    owner: ['', [Validators.required, Validators.maxLength(80)]],
    dueAt: [new Date(Date.now() + 7 * 86400000).toISOString().slice(0, 10), Validators.required],
  });

  readonly objectiveForm=this.formBuilder.nonNullable.group({
    ownerTeam:['',[Validators.required,Validators.maxLength(80)]],availabilityTargetPercent:[99.9,[Validators.required,Validators.min(90),Validators.max(100)]],
    acknowledgementTargetMinutes:[5,[Validators.required,Validators.min(1)]],resolutionTargetMinutes:[45,[Validators.required,Validators.min(1)]],
    monthlyErrorBudgetMinutes:[43,[Validators.required,Validators.min(1)]],fastBurnThreshold:[14,[Validators.required,Validators.min(1)]],
    slowBurnThreshold:[6,[Validators.required,Validators.min(1)]],enabled:[true],
  });

  readonly maintenanceForm=this.formBuilder.nonNullable.group({
    service:['',Validators.required],title:['',[Validators.required,Validators.maxLength(160)]],
    startsAt:[new Date(Date.now()+3600000).toISOString().slice(0,16),Validators.required],
    endsAt:[new Date(Date.now()+7200000).toISOString().slice(0,16),Validators.required],
  });

  constructor() {
    this.filters.valueChanges.subscribe((filters) => {
      this.store.setFilters({
        query: filters.query ?? '',
        severity: filters.severity ?? 'All',
        status: filters.status ?? 'All',
        ownerTeam: filters.ownerTeam ?? '',
      });
    });
    effect(() => {
      const postmortem = this.store.postmortem();
      if (postmortem) {
        this.postmortemForm.patchValue({ owner: postmortem.owner, status: postmortem.status,
          executiveSummary: postmortem.executiveSummary, rootCause: postmortem.rootCause,
          detection: postmortem.detection, resolution: postmortem.resolution,
          lessonsLearned: postmortem.lessonsLearned }, {emitEvent:false});
      } else {
        this.postmortemForm.reset({ owner:'', status:'Draft', executiveSummary:'', rootCause:'',
          detection:'', resolution:'', lessonsLearned:'' }, {emitEvent:false});
      }
    });
  }

  openCreateDialog(): void {
    this.dialog?.nativeElement.showModal();
  }

  closeCreateDialog(): void {
    this.dialog?.nativeElement.close();
    this.createForm.reset({
      title: '', summary: '', severity: 'SEV-2', service: '', ownerTeam: '', assignee: '',
    });
  }

  createIncident(): void {
    if (this.createForm.invalid) {
      this.createForm.markAllAsTouched();
      return;
    }
    this.store.create(this.createForm.getRawValue(), () => this.closeCreateDialog());
  }

  updateStatus(event: Event): void {
    const selected = this.store.selected();
    if (!selected) return;
    const status = (event.target as HTMLSelectElement).value as IncidentStatus;
    if (status !== selected.status) this.store.updateStatus(selected.id, status);
  }

  addNote(): void {
    const selected = this.store.selected();
    if (!selected || this.noteForm.invalid) {
      this.noteForm.markAllAsTouched();
      return;
    }
    this.store.addTimelineEntry(selected.id, this.noteForm.controls.note.value, () => this.noteForm.reset());
  }

  selectIncident(id: string): void {
    this.store.select(id);
    const incident = this.store.selected();
    if (incident) this.tagsForm.controls.tags.setValue(incident.tags.join(', '));
  }

  updateAssignment(): void {
    const incident = this.store.selected();
    if (!incident || this.assignmentForm.invalid) return;
    this.store.updateAssignment(incident.id, this.assignmentForm.controls.assignee.value,
      () => this.assignmentForm.reset());
  }

  addResponder(): void {
    const incident = this.store.selected();
    if (!incident || this.responderForm.invalid) return;
    const { name, role } = this.responderForm.getRawValue();
    this.store.addResponder(incident.id, name, role, () => this.responderForm.reset());
  }

  removeResponder(responderId: string): void {
    const incident = this.store.selected();
    if (incident) this.store.removeResponder(incident.id, responderId);
  }

  updateTags(): void {
    const incident = this.store.selected();
    if (!incident || this.tagsForm.invalid) return;
    const tags = this.tagsForm.controls.tags.value.split(',').map((tag) => tag.trim()).filter(Boolean);
    this.store.updateTags(incident.id, tags,
      () => this.tagsForm.controls.tags.setValue(tags.join(', ')));
  }
  savePostmortem():void { if(this.postmortemForm.invalid){this.postmortemForm.markAllAsTouched();return;}
    this.store.savePostmortem({...this.postmortemForm.getRawValue(),expectedVersion:this.store.postmortem()?.version??null}); }
  addActionItem():void { if(this.actionForm.invalid){this.actionForm.markAllAsTouched();return;}const value=this.actionForm.getRawValue();
    this.store.addActionItem(value.title,value.owner,new Date(`${value.dueAt}T17:00:00Z`).toISOString(),()=>this.actionForm.reset({title:'',owner:'',dueAt:new Date(Date.now()+7*86400000).toISOString().slice(0,10)})); }
  updateActionStatus(actionId:string,event:Event,owner:string,dueAt:string):void { this.store.updateActionItem(actionId,(event.target as HTMLSelectElement).value as ActionItemStatus,owner,dueAt); }
  editObjective(objective:ServiceObjective):void{this.editingObjective=objective;this.objectiveForm.reset({ownerTeam:objective.ownerTeam,
    availabilityTargetPercent:objective.availabilityTargetPercent,acknowledgementTargetMinutes:objective.acknowledgementTargetMinutes,
    resolutionTargetMinutes:objective.resolutionTargetMinutes,monthlyErrorBudgetMinutes:objective.monthlyErrorBudgetMinutes,
    fastBurnThreshold:objective.fastBurnThreshold,slowBurnThreshold:objective.slowBurnThreshold,enabled:objective.enabled});this.objectiveDialog?.nativeElement.showModal();}
  closeObjectiveDialog():void{this.objectiveDialog?.nativeElement.close();this.editingObjective=null;}
  saveObjective():void{if(!this.editingObjective||this.objectiveForm.invalid){this.objectiveForm.markAllAsTouched();return;}const objective=this.editingObjective;
    this.store.updateObjective(objective.id,{...this.objectiveForm.getRawValue(),expectedVersion:objective.version},()=>this.closeObjectiveDialog());}
  createMaintenanceWindow():void{if(this.maintenanceForm.invalid){this.maintenanceForm.markAllAsTouched();return;}const value=this.maintenanceForm.getRawValue();
    this.store.createMaintenanceWindow(value.service,value.title,new Date(value.startsAt).toISOString(),new Date(value.endsAt).toISOString(),()=>this.maintenanceForm.controls.title.reset());}
  insertCommand(command: string): void { this.noteForm.controls.note.setValue(command); }

  initials(name: string): string {
    return name.split(' ').map((part) => part[0]).join('').slice(0, 2).toUpperCase();
  }
}
