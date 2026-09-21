import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { BehaviorSubject, EMPTY, Observable, catchError, debounceTime, finalize, forkJoin, switchMap, tap } from 'rxjs';
import { ActionItemStatus, AnalyticsOverview, CreateIncidentRequest, Incident, IncidentFilters, IncidentMetrics, IncidentStatus, MaintenanceWindow, Postmortem, ReliabilitySignal, SavePostmortemRequest, ServiceObjective, UpdateServiceObjectiveRequest } from '../models/incident';
import { IncidentGateway, IncidentVersionConflictError } from './incident-gateway';
import { IncidentRealtime } from './incident-realtime';

const INITIAL_FILTERS: IncidentFilters={query:'',severity:'All',status:'All',ownerTeam:''};
interface QueueRequest { filters:IncidentFilters; page:number; pageSize:number; }

/** Coordinates the server queue, selected incident, mutations, and real-time invalidation. */
@Injectable()
export class IncidentStore {
  private readonly destroyRef=inject(DestroyRef); private readonly gateway=inject(IncidentGateway); private readonly realtime=inject(IncidentRealtime);
  private readonly request=new BehaviorSubject<QueueRequest>({filters:INITIAL_FILTERS,page:1,pageSize:10});
  readonly incidents=signal<Incident[]>([]); readonly selectedId=signal<string|null>(null); readonly loading=signal(true);
  readonly saving=signal(false); readonly error=signal<string|null>(null); readonly conflict=signal(false);
  readonly analytics=signal<AnalyticsOverview|null>(null); readonly analyticsLoading=signal(true);
  readonly postmortem=signal<Postmortem|null>(null); readonly postmortemLoading=signal(false);
  readonly objectives=signal<ServiceObjective[]>([]); readonly reliabilitySignals=signal<ReliabilitySignal[]>([]);
  readonly maintenanceWindows=signal<MaintenanceWindow[]>([]); readonly automationLoading=signal(true);
  readonly total=signal(0); readonly page=signal(1); readonly pageSize=signal(10); readonly totalPages=signal(0);
  readonly presence=this.realtime.presence; readonly liveConnected=this.realtime.connected;
  readonly selected=computed(()=>this.incidents().find(i=>i.id===this.selectedId())??null);
  readonly metrics=computed<IncidentMetrics>(()=>({active:this.analytics()?.metrics.activeIncidents??this.incidents().filter(i=>i.status!=='Resolved').length,
    critical:this.incidents().filter(i=>i.severity==='SEV-1'&&i.status!=='Resolved').length,
    monitoring:this.incidents().filter(i=>i.status==='Monitoring').length,meanTimeToAcknowledgeMinutes:this.analytics()?.metrics.meanTimeToAcknowledgeMinutes??0}));
  constructor(){
    this.gateway.analytics(30).pipe(finalize(()=>this.analyticsLoading.set(false)),takeUntilDestroyed(this.destroyRef)).subscribe({
      next:overview=>this.analytics.set(overview),error:()=>this.error.set('Reliability analytics could not be loaded.'),});
    this.refreshAutomation();
    this.realtime.start(event=>{if(event.incidentId===this.selectedId())this.gateway.get(event.incidentId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({next:i=>this.merge(i)});else this.refresh();});
    this.request.pipe(debounceTime(100),tap(()=>{this.loading.set(true);this.error.set(null);}),switchMap(r=>this.gateway.list(r.filters,r.page,r.pageSize).pipe(
      catchError(()=>{this.error.set('Incident data could not be loaded. Check the API and try again.');return EMPTY;}),finalize(()=>this.loading.set(false)))),takeUntilDestroyed(this.destroyRef))
      .subscribe(result=>{this.incidents.set(result.items);this.total.set(result.total);this.page.set(result.page);this.pageSize.set(result.pageSize);this.totalPages.set(result.totalPages);
        const selected=result.items.some(i=>i.id===this.selectedId())?this.selectedId():result.items[0]?.id??null;this.selectedId.set(selected);if(selected){this.realtime.watchIncident(selected);this.loadPostmortem(selected);}});
  }
  setFilters(filters:IncidentFilters):void{this.request.next({...this.request.value,filters,page:1});}
  previousPage():void{if(this.page()>1)this.request.next({...this.request.value,page:this.page()-1});}
  nextPage():void{if(this.page()<this.totalPages())this.request.next({...this.request.value,page:this.page()+1});}
  select(id:string):void{this.selectedId.set(id);this.realtime.watchIncident(id);this.loadPostmortem(id);}
  create(request:CreateIncidentRequest,done:()=>void):void{this.run(this.gateway.create(request),done);}
  updateStatus(id:string,status:IncidentStatus):void{const i=this.get(id);if(i)this.run(this.gateway.updateStatus(id,status,i.version));}
  addTimelineEntry(id:string,message:string,done:()=>void):void{const i=this.get(id);if(i)this.run(this.gateway.addTimelineEntry(id,message,i.version),done);}
  updateAssignment(id:string,assignee:string,done:()=>void):void{const i=this.get(id);if(i)this.run(this.gateway.updateAssignment(id,assignee,i.version),done);}
  addResponder(id:string,name:string,role:string,done:()=>void):void{const i=this.get(id);if(i)this.run(this.gateway.addResponder(id,name,role,i.version),done);}
  removeResponder(id:string,responderId:string):void{const i=this.get(id);if(i)this.run(this.gateway.removeResponder(id,responderId,i.version));}
  updateTags(id:string,tags:string[],done:()=>void):void{const i=this.get(id);if(i)this.run(this.gateway.updateTags(id,tags,i.version),done);}
  savePostmortem(request:SavePostmortemRequest,done?:()=>void):void{const id=this.selectedId();if(id)this.runPostmortem(this.gateway.savePostmortem(id,request),done);}
  addActionItem(title:string,owner:string,dueAt:string,done?:()=>void):void{const id=this.selectedId(),postmortem=this.postmortem();
    if(id&&postmortem)this.runPostmortem(this.gateway.addActionItem(id,title,owner,dueAt,postmortem.version),done);}
  updateActionItem(actionId:string,status:ActionItemStatus,owner:string,dueAt:string):void{const id=this.selectedId(),postmortem=this.postmortem();
    if(id&&postmortem)this.runPostmortem(this.gateway.updateActionItem(id,actionId,status,owner,dueAt,postmortem.version));}
  exportEvidence(format:'json'|'csv'):void{const id=this.selectedId();if(!id)return;this.gateway.exportEvidence(id,format).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({next:blob=>{
    const url=URL.createObjectURL(blob);const link=document.createElement('a');link.href=url;link.download=`${id}-evidence.${format}`;link.click();URL.revokeObjectURL(url);},
    error:()=>this.error.set('The evidence package could not be exported.')});}
  updateObjective(id:string,request:UpdateServiceObjectiveRequest,done?:()=>void):void{this.runAutomation(this.gateway.updateObjective(id,request),
    item=>{this.objectives.set(this.objectives().map(objective=>objective.id===item.id?item:objective));done?.();},'The service objective could not be saved.');}
  evaluateReliability():void{this.runAutomation(this.gateway.evaluateReliability(),result=>{
    const merged=new Map(this.reliabilitySignals().map(item=>[item.id,item]));for(const item of result.signals)merged.set(item.id,item);
    this.reliabilitySignals.set([...merged.values()].sort((a,b)=>b.lastObservedAt.localeCompare(a.lastObservedAt)));},'Reliability evaluation failed.');}
  acknowledgeSignal(signal:ReliabilitySignal):void{this.runAutomation(this.gateway.acknowledgeSignal(signal.id,signal.version),
    updated=>this.replaceSignal(updated),'The signal could not be acknowledged.');}
  promoteSignal(signal:ReliabilitySignal):void{this.runAutomation(this.gateway.promoteSignal(signal.id,'Dan Fuhr',signal.version),result=>{
    this.replaceSignal(result.signal);this.merge(result.incident);this.refresh();},'The signal could not be promoted.');}
  createMaintenanceWindow(service:string,title:string,startsAt:string,endsAt:string,done?:()=>void):void{this.runAutomation(
    this.gateway.createMaintenanceWindow(service,title,startsAt,endsAt),item=>{this.maintenanceWindows.set([item,...this.maintenanceWindows()]);done?.();},
    'The maintenance window could not be created.');}
  deleteMaintenanceWindow(window:MaintenanceWindow):void{this.runAutomation(this.gateway.deleteMaintenanceWindow(window.id,window.version),()=>
    this.maintenanceWindows.set(this.maintenanceWindows().filter(item=>item.id!==window.id)),'The maintenance window could not be removed.');}
  private refresh():void{this.request.next({...this.request.value});}
  private run(operation:ReturnType<IncidentGateway['create']>,done?:()=>void):void{this.saving.set(true);this.error.set(null);this.conflict.set(false);
    operation.pipe(finalize(()=>this.saving.set(false)),takeUntilDestroyed(this.destroyRef)).subscribe({next:i=>{this.merge(i);done?.();},error:e=>this.handleError(e)});}
  private handleError(error:unknown):void{const current=error instanceof IncidentVersionConflictError?error.current:error instanceof HttpErrorResponse&&error.status===409?error.error?.current as Incident|undefined:undefined;
    if(current){this.merge(current);this.conflict.set(true);this.error.set('This incident changed in another session. The latest version is loaded; review it before retrying.');return;}
    if(error instanceof HttpErrorResponse&&error.status===400){this.error.set(error.error?.errors?.command?.[0]??'The timeline command is not valid.');return;}this.error.set('The change could not be saved. Please try again.');}
  private runPostmortem(operation:ReturnType<IncidentGateway['savePostmortem']>,done?:()=>void):void{this.saving.set(true);this.error.set(null);
    operation.pipe(finalize(()=>this.saving.set(false)),takeUntilDestroyed(this.destroyRef)).subscribe({next:item=>{this.postmortem.set(item);done?.();},
      error:error=>{if(error instanceof HttpErrorResponse&&error.status===409){this.postmortem.set(error.error?.current??this.postmortem());this.error.set('The postmortem changed in another session. Review the latest version before retrying.');}
        else this.error.set('The postmortem change could not be saved.');}});}
  private loadPostmortem(id:string):void{this.postmortem.set(null);this.postmortemLoading.set(true);this.gateway.getPostmortem(id).pipe(finalize(()=>this.postmortemLoading.set(false)),takeUntilDestroyed(this.destroyRef))
    .subscribe({next:item=>{if(this.selectedId()===id)this.postmortem.set(item);},error:()=>this.error.set('The postmortem could not be loaded.')});}
  private refreshAutomation():void{this.automationLoading.set(true);forkJoin({objectives:this.gateway.listObjectives(),signals:this.gateway.listSignals(),windows:this.gateway.listMaintenanceWindows()})
    .pipe(finalize(()=>this.automationLoading.set(false)),takeUntilDestroyed(this.destroyRef)).subscribe({next:result=>{this.objectives.set(result.objectives);
      this.reliabilitySignals.set(result.signals);this.maintenanceWindows.set(result.windows);},error:()=>this.error.set('Reliability automation data could not be loaded.')});}
  private runAutomation<T>(operation:Observable<T>,next:(value:T)=>void,message:string):void{this.saving.set(true);this.error.set(null);operation.pipe(
    finalize(()=>this.saving.set(false)),takeUntilDestroyed(this.destroyRef)).subscribe({next,error:error=>{if(error instanceof HttpErrorResponse&&error.status===409)
      this.error.set('This reliability record changed in another session. Refresh before retrying.');else this.error.set(message);}});}
  private replaceSignal(updated:ReliabilitySignal):void{this.reliabilitySignals.set(this.reliabilitySignals().map(item=>item.id===updated.id?updated:item));}
  private merge(updated:Incident):void{const list=this.incidents(),exists=list.some(i=>i.id===updated.id);this.incidents.set(exists?list.map(i=>i.id===updated.id?updated:i):[updated,...list].slice(0,this.pageSize()));
    this.selectedId.set(updated.id);this.realtime.watchIncident(updated.id);}
  private get(id:string):Incident|undefined{return this.incidents().find(i=>i.id===id);}
}
