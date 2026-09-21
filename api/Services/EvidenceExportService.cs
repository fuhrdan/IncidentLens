using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IncidentLens.Api.Contracts;

namespace IncidentLens.Api.Services;

public sealed record EvidenceExportResult(byte[] Content, string ContentType, string FileName);

public sealed class EvidenceExportService(
    IncidentService incidents,
    PostmortemService postmortems,
    TimeProvider clock)
{
    public async Task<EvidenceExportResult?> ExportAsync(string id, string format, CancellationToken token)
    {
        var incident = await incidents.GetAsync(id, token);
        if (incident is null) return null;
        var audit = await incidents.ListAuditAsync(id, token) ?? [];
        var postmortem = await postmortems.GetAsync(id, token);
        var package = new EvidencePackage(clock.GetUtcNow(), Activity.Current?.TraceId.ToString() ?? string.Empty,
            incident, audit, postmortem);
        if (format.Equals("csv", StringComparison.OrdinalIgnoreCase))
            return new EvidenceExportResult(Encoding.UTF8.GetBytes(ToCsv(package)), "text/csv", $"{id}-evidence.csv");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return new EvidenceExportResult(JsonSerializer.SerializeToUtf8Bytes(package, options),
            "application/json", $"{id}-evidence.json");
    }

    private static string ToCsv(EvidencePackage package)
    {
        var rows = new List<string> { "recordType,timestamp,actor,action,details,owner,status" };
        rows.Add(Row("incident", package.Incident.DeclaredAt.ToString("O"), package.Incident.Assignee,
            "declared", package.Incident.Title, package.Incident.OwnerTeam, package.Incident.Status.ToString()));
        rows.AddRange(package.Audit.Select(item => Row("audit", item.OccurredAt.ToString("O"), item.Actor,
            item.Action, item.Details, string.Empty, item.IncidentVersion.ToString())));
        if (package.Postmortem is not null)
        {
            rows.Add(Row("postmortem", package.Postmortem.UpdatedAt.ToString("O"), package.Postmortem.Owner,
                "postmortem", package.Postmortem.ExecutiveSummary, package.Postmortem.Owner, package.Postmortem.Status.ToString()));
            rows.AddRange(package.Postmortem.ActionItems.Select(item => Row("action-item", item.CreatedAt.ToString("O"),
                item.Owner, "action", item.Title, item.Owner, item.Status.ToString())));
        }
        return string.Join(Environment.NewLine, rows);
    }

    private static string Row(params string[] values) => string.Join(',', values.Select(value =>
        $"\"{value.Replace("\"", "\"\"")}\""));
}
