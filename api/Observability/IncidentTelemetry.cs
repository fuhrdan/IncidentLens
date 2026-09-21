using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace IncidentLens.Api.Observability;

public sealed class IncidentTelemetry : IDisposable
{
    public const string SourceName = "IncidentLens.Api";
    public ActivitySource Activities { get; } = new(SourceName);
    private readonly Meter meter = new(SourceName, "0.9.0");
    public Counter<long> RateLimitRejections { get; }
    public Counter<long> OutboxPublished { get; }
    public Counter<long> OutboxRetries { get; }
    public Counter<long> Mutations { get; }
    public Counter<long> AlertsIngested { get; }
    public Counter<long> PostmortemsUpdated { get; }
    public Histogram<double> AnalyticsDurationMilliseconds { get; }
    public Counter<long> ReliabilityEvaluations { get; }
    public Counter<long> ReliabilitySignalsCreated { get; }
    public Counter<long> ReliabilitySignalsPromoted { get; }

    public IncidentTelemetry()
    {
        RateLimitRejections = meter.CreateCounter<long>("incidentlens.http.rate_limit_rejections");
        OutboxPublished = meter.CreateCounter<long>("incidentlens.outbox.published");
        OutboxRetries = meter.CreateCounter<long>("incidentlens.outbox.retries");
        Mutations = meter.CreateCounter<long>("incidentlens.incident.mutations");
        AlertsIngested = meter.CreateCounter<long>("incidentlens.alerts.ingested");
        PostmortemsUpdated = meter.CreateCounter<long>("incidentlens.postmortems.updated");
        AnalyticsDurationMilliseconds = meter.CreateHistogram<double>("incidentlens.analytics.duration", "ms");
        ReliabilityEvaluations = meter.CreateCounter<long>("incidentlens.reliability.evaluations");
        ReliabilitySignalsCreated = meter.CreateCounter<long>("incidentlens.reliability.signals.created");
        ReliabilitySignalsPromoted = meter.CreateCounter<long>("incidentlens.reliability.signals.promoted");
    }

    public void Dispose() { Activities.Dispose(); meter.Dispose(); }
}
