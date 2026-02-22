using System.Text.Json;

namespace Api.Contracts;

public sealed record IngestionRequest(string TenantId, IReadOnlyList<IngestionEventRequest> Events)
{
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(TenantId))
        {
            errors["tenantId"] = ["tenantId is required"];
        }

        if (Events is null || Events.Count == 0)
        {
            errors["events"] = ["at least one event is required"];
            return errors;
        }

        var eventErrors = new List<string>();
        for (var i = 0; i < Events.Count; i++)
        {
            var item = Events[i];
            if (string.IsNullOrWhiteSpace(item.Type))
            {
                eventErrors.Add($"events[{i}].type is required");
            }

            if (item.Timestamp == default)
            {
                eventErrors.Add($"events[{i}].timestamp must be ISO-8601 date");
            }
        }

        if (eventErrors.Count > 0)
        {
            errors["events"] = eventErrors.ToArray();
        }

        return errors;
    }
}

public sealed record IngestionEventRequest(string Type, DateTimeOffset Timestamp, JsonElement Payload);
