import { Injectable, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { IncidentChangedEvent, PresenceUser } from '../models/incident';
import { environment } from '../../../environments/environment';
import { AuthService } from './auth.service';
import { inject } from '@angular/core';

/** Owns SignalR connection and room membership separately from HTTP persistence. */
@Injectable({ providedIn: 'root' })
export class IncidentRealtime {
  private readonly auth = inject(AuthService);
  readonly connected = signal(false); readonly presence = signal<PresenceUser[]>([]);
  private connection?: HubConnection; private activeIncidentId: string | null = null;
  private updateHandler: (event: IncidentChangedEvent) => void = () => undefined;
  start(onUpdate: (event: IncidentChangedEvent) => void): void {
    this.updateHandler = onUpdate; if (environment.demoMode) { this.connected.set(true); return; }
    const url = `${environment.apiBaseUrl.replace(/\/api\/?$/, '')}/hubs/incidents`;
    this.connection = new HubConnectionBuilder().withUrl(url,{accessTokenFactory:()=>this.auth.token()})
      .withAutomaticReconnect([0,1000,3000,8000]).configureLogging(LogLevel.Warning).build();
    this.connection.on('IncidentUpdated',(event:IncidentChangedEvent)=>this.updateHandler(event));
    this.connection.on('PresenceChanged',(id:string,users:PresenceUser[])=>{if(id===this.activeIncidentId)this.presence.set(users);});
    this.connection.onreconnecting(()=>this.connected.set(false)); this.connection.onclose(()=>this.connected.set(false));
    this.connection.onreconnected(()=>{this.connected.set(true);void this.join();});
    void this.connection.start().then(()=>{this.connected.set(true);return this.join();}).catch(()=>this.connected.set(false));
  }
  watchIncident(id: string): void { const previous=this.activeIncidentId; this.activeIncidentId=id;
    this.presence.set(environment.demoMode?this.demoPresence():[]); if(!this.connection||this.connection.state!==HubConnectionState.Connected)return;
    if(previous&&previous!==id)void this.connection.invoke('LeaveIncident',previous); void this.join(); }
  private join(): Promise<void> { return !this.connection||!this.activeIncidentId||this.connection.state!==HubConnectionState.Connected
    ? Promise.resolve() : this.connection.invoke('JoinIncident',this.activeIncidentId); }
  private demoPresence(): PresenceUser[] { const now=new Date().toISOString(); return [
    {connectionId:'demo-df',name:'Dan Fuhr',role:'Commander',joinedAt:now},
    {connectionId:'demo-mc',name:'Maya Chen',role:'Viewer',joinedAt:now},
    {connectionId:'demo-rs',name:'Ravi Shah',role:'Viewer',joinedAt:now}]; }
}
