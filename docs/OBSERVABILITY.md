# Observability

IncidentLens v0.9.0 emits OpenTelemetry traces and metrics under the service name `IncidentLens.Api`. ASP.NET Core request traces, outbound HTTP traces, HTTP metrics, and runtime metrics are enabled by default. Custom telemetry includes:

- `incidentlens.http.rate_limit_rejections`
- `incidentlens.outbox.published`
- `incidentlens.outbox.retries`
- `incidentlens.incident.mutations`
- `incidentlens.alerts.ingested`
- `incidentlens.postmortems.updated`
- `incidentlens.analytics.duration` in milliseconds
- `incidentlens.reliability.evaluations`
- `incidentlens.reliability.signals.created`
- `incidentlens.reliability.signals.promoted`
- `incident.create`, `incident.mutate`, `alert.ingest`, `postmortem.upsert`, `analytics.overview`, and `reliability.evaluate` activities

Set `OpenTelemetry__OtlpEndpoint` to an OTLP/gRPC collector such as `http://localhost:4317`. When the setting is empty, instrumentation still runs but no network exporter is registered.

Every HTTP response includes `X-Correlation-ID`. Supplying an inbound identifier preserves it only when it is 1..64 ASCII alphanumeric, hyphen or underscore characters; otherwise a new identifier is generated. This prevents untrusted control characters or long values from being reflected or logged. Without it, the active trace ID becomes the correlation ID.

Useful resource attributes include `service.name=IncidentLens.Api` and `service.version=0.9.0`. Incident IDs and mutation versions are attached to custom activities, while counters use bounded tags such as mutation kind, signal status, severity, and postmortem status.
