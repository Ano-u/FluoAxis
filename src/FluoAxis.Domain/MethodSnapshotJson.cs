namespace FluoAxis.Domain;

using System.Text.Json;

/// <summary>
/// Canonical JSON writer for <see cref="BrafMethodSnapshot"/>. Property order
/// is fixed by construction, output has no whitespace, and numbers use the
/// shortest round-trippable invariant form, so equal snapshots always
/// serialize to identical bytes.
/// </summary>
internal static class MethodSnapshotJson
{
    public static string WriteCanonicalJson(BrafMethodSnapshot snapshot)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteSnapshot(writer, snapshot);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteSnapshot(Utf8JsonWriter writer, BrafMethodSnapshot snapshot)
    {
        writer.WriteStartObject();

        writer.WriteString("id", snapshot.MethodId);
        writer.WriteString("version", snapshot.Version);
        writer.WriteNumber("endpointMinutes", snapshot.EndpointMinutes);
        writer.WriteNumber("endpointToleranceSeconds", snapshot.EndpointToleranceSeconds);

        writer.WriteStartObject("plateau");
        writer.WriteNumber("startMinutes", snapshot.Plateau.StartMinutes);
        writer.WriteNumber("endMinutes", snapshot.Plateau.EndMinutes);
        writer.WriteNumber("relativeRangeMax", snapshot.Plateau.RelativeRangeMax);
        writer.WriteNumber("minWindowPoints", snapshot.Plateau.MinWindowPoints);
        writer.WriteEndObject();

        writer.WriteStartObject("ntc");
        writer.WriteNumber("normalizedMin", snapshot.Ntc.NormalizedMin);
        writer.WriteNumber("normalizedMax", snapshot.Ntc.NormalizedMax);
        writer.WriteEndObject();

        writer.WriteStartObject("wt");
        writer.WriteNumber("famMeanMaxExclusive", snapshot.Wt.FamMeanMaxExclusive);
        writer.WriteNumber("roxMeanMaxExclusive", snapshot.Wt.RoxMeanMaxExclusive);
        writer.WriteEndObject();

        writer.WriteStartObject("classification");
        writer.WriteNumber("drPrimeThresholdInclusive", snapshot.Classification.DrPrimeThresholdInclusive);
        writer.WriteString("positiveLabel", snapshot.Classification.PositiveLabel);
        writer.WriteString("negativeLabel", snapshot.Classification.NegativeLabel);
        writer.WriteEndObject();

        writer.WriteStartObject("replicates");
        writer.WriteBoolean("requireClassificationConsistency", snapshot.Replicates.RequireClassificationConsistency);
        writer.WriteEndObject();

        writer.WriteStartObject("roles");
        writer.WriteStartObject("minimumWells");
        writer.WriteNumber("ntc", snapshot.Roles.NtcMinimumWells);
        writer.WriteNumber("positive100", snapshot.Roles.Positive100MinimumWells);
        writer.WriteNumber("wt", snapshot.Roles.WildTypeMinimumWells);
        writer.WriteNumber("lqc01Percent", snapshot.Roles.Lqc01PercentMinimumWells);
        writer.WriteEndObject();
        writer.WriteEndObject();

        writer.WriteEndObject();
    }
}
