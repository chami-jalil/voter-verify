using System.Text;
using VoterDataImporter.Validation;
using VoterVerification.Configuration;

namespace VoterDataImporter.Import;

public sealed class ImportRequest
{
    public required string CsvText { get; init; }

    public string WardId { get; init; } = AppConfiguration.Ward10Id;

    public bool Write { get; init; }

    public string AccountLabel { get; init; } = "";
}

public sealed class ImportRunResult
{
    public required ValidationReport Report { get; init; }

    public ImportPlan? Plan { get; init; }

    public BatchWriteResult? WriteResult { get; init; }

    public bool AzureChecked { get; init; }

    public bool ConfirmationAccepted { get; init; }

    public required int ExitCode { get; init; }

    public required string Text { get; init; }
}

public static class ImportRunner
{
    public static async Task<ImportRunResult> RunAsync(
        ImportRequest request,
        IVoterTableStore? store,
        Func<string, bool>? confirm,
        CancellationToken cancellationToken)
    {
        var report = VoterCsvValidator.ValidateCsv(request.CsvText, request.WardId);
        var text = new StringBuilder();
        text.Append(request.Write ? "Import." : "Dry run. No records were written.");
        text.AppendLine();
        text.Append(report.Format());

        if (report.HasBlockingErrors)
        {
            text.AppendLine("Invalid rows were not imported.");
            return Finish(report, null, null, false, false, 1, text);
        }

        if (request.Write && store is null)
        {
            text.AppendLine("Import needs a storage account name. Nothing was written.");
            return Finish(report, null, null, false, false, 2, text);
        }

        ImportPlan? plan = null;
        var azureChecked = false;
        if (store is not null && report.ValidEntities.Count > 0)
        {
            var existing = new List<VoterVerification.Data.VoterTableEntity>();
            foreach (var wardId in report.ValidEntities.Select(entity => entity.PartitionKey).Distinct(StringComparer.Ordinal))
            {
                existing.AddRange(await store.ReadPartitionAsync(wardId, cancellationToken));
            }

            plan = ImportPlanner.Plan(report.ValidEntities, existing);
            azureChecked = true;
            text.AppendLine($"Existing rows checked in {AppConfiguration.VotersTableName}.");
        }
        else if (store is null)
        {
            plan = ImportPlanner.Plan(report.ValidEntities, []);
            text.AppendLine("Azure was not contacted. Existing rows were not checked.");
        }

        if (plan is not null)
        {
            text.AppendLine($"{(request.Write ? "Ready to insert" : "Would insert")}: {plan.ToInsert.Count}");
            text.AppendLine($"Already stored, left unchanged: {plan.AlreadyStored.Count}");
            text.AppendLine($"Different existing rows, left unchanged: {plan.DifferentExisting.Count}");
            foreach (var row in plan.DifferentExisting)
            {
                text.AppendLine($"Conflict {row.Incoming.PartitionKey}/{row.Incoming.RowKey}: stored voter was kept.");
            }
        }

        if (!request.Write || plan is null)
        {
            return Finish(report, plan, null, azureChecked, false, 0, text);
        }

        if (plan.ToInsert.Count == 0)
        {
            text.AppendLine("Nothing new to insert.");
            return Finish(report, plan, null, azureChecked, false, 0, text);
        }

        var prompt = $"Type IMPORT to insert {plan.ToInsert.Count} new voter records into {AppConfiguration.VotersTableName} on account {request.AccountLabel}. Existing rows stay unchanged. VoterVerifications is not written.";
        var accepted = confirm?.Invoke(prompt) == true;
        if (!accepted)
        {
            text.AppendLine("Import cancelled. Nothing was written.");
            return Finish(report, plan, null, azureChecked, false, 1, text);
        }

        var written = await store!.InsertAsync(plan.ToInsert, cancellationToken);
        text.AppendLine($"Inserted: {written.Inserted.Count}");
        text.AppendLine($"Skipped existing: {written.Skipped.Count}");
        text.AppendLine($"Failed: {written.Failed.Count}");
        foreach (var skipped in written.Skipped)
        {
            text.AppendLine($"Skipped {skipped.Entity.PartitionKey}/{skipped.Entity.RowKey}: {skipped.Reason}");
        }

        foreach (var failed in written.Failed)
        {
            text.AppendLine($"Failed {failed.Entity.PartitionKey}/{failed.Entity.RowKey}: {failed.Reason}");
        }

        var exit = written.Failed.Count == 0 ? 0 : 1;
        return Finish(report, plan, written, azureChecked, true, exit, text);
    }

    private static ImportRunResult Finish(
        ValidationReport report,
        ImportPlan? plan,
        BatchWriteResult? writeResult,
        bool azureChecked,
        bool confirmationAccepted,
        int exitCode,
        StringBuilder text) =>
        new()
        {
            Report = report,
            Plan = plan,
            WriteResult = writeResult,
            AzureChecked = azureChecked,
            ConfirmationAccepted = confirmationAccepted,
            ExitCode = exitCode,
            Text = text.ToString()
        };
}
