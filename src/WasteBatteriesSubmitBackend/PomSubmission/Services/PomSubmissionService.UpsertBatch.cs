using FluentValidation;
using FluentValidation.Results;
using System.Diagnostics.CodeAnalysis;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Requests;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Responses;
using WasteBatteriesSubmitBackend.PomSubmission.Validators;
using WasteBatteriesSubmitBackend.Utils.Logging;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.PomSubmission.Services;

// A batch of submissions. Each item is created or amended. The submissions and one outbox
// event per scheme and compliance year commit or roll back together.
public partial class PomSubmissionService
{
    // Saves many submissions. A submission witn no existing matching key (scheme id, member id, year & quarter) is created, else amended.
    // Every item is checked, and each error names that item's position.
    // The response is one short result per item, in request order - returning the full objects would cause payload limit issues.
    // Each response item includes the new id, the key, the request index, and whether that item was created or amended.
    // Current rows are read in one query. Ids and the timestamp are allocated before the transaction.
    // The callback can run more than once.
    public async Task<IReadOnlyList<PomSubmissionUpsertResult>> UpsertBatchAsync(
        List<PomSubmissionRequest> requests,
        CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareBatchAsync(requests, cancellationToken);
        if (prepared.Failures.Count > 0)
        {
            throw new ValidationException(prepared.Failures);
        }

        var pending = new List<(PomSubmissionRequest Request, PomSubmissionKey Key)>(prepared.Requests.Count);
        foreach (var request in prepared.Requests)
        {
            pending.Add((request, PomSubmissionKey.From(request)));
        }

        var currents = await documents.Repository.FindCurrentAsync(
            [.. pending.Select(item => item.Key)],
            cancellationToken);
        var currentByKey = IndexCurrents(currents);
        var savedAtUtc = timeProvider.UtcNow;
        var submissions = new List<PomSubmissionModel>(pending.Count);
        var amended = new List<bool>(pending.Count);
        var results = new List<PomSubmissionUpsertResult>(pending.Count);
        var currentIds = new List<string>();

        foreach (var (request, key) in pending)
        {
            PomSubmissionModel submission;
            PomSubmissionUpsertAction action;
            if (currentByKey.TryGetValue(key, out var current))
            {
                currentIds.Add(current.Id);
                action = PomSubmissionUpsertAction.Amended;
                submission = documents.Mapper.ToNewSubmission(
                    request,
                    savedAtUtc,
                    version: current.Version + 1,
                    supersedesSubmissionId: current.Id);
            }
            else
            {
                action = PomSubmissionUpsertAction.Created;
                submission = documents.Mapper.ToNewSubmission(request, savedAtUtc);
            }

            submissions.Add(submission);
            amended.Add(action == PomSubmissionUpsertAction.Amended);
            results.Add(new PomSubmissionUpsertResult
            {
                RequestIndex = results.Count,
                Id = submission.Id,
                SchemeOrgId = key.SchemeOrgId,
                MemberOrgId = key.MemberOrgId,
                CompliancePeriodYear = key.CompliancePeriodYear,
                CompliancePeriodQuarter = key.CompliancePeriodQuarter,
                Action = action
            });
        }

        var outboxEvents = OutboxFor(
            submissions.Select((submission, index) => (submission, amended[index])),
            savedAtUtc);

        try
        {
            await ExecuteSaveAsync(
                async (transaction, transactionCancellationToken) =>
                {
                    await WriteBatchAsync(
                        transaction,
                        currentIds,
                        submissions,
                        savedAtUtc,
                        transactionCancellationToken);

                    await documents.Outbox.AddAsync(
                        transaction.Session,
                        outboxEvents,
                        transactionCancellationToken);
                    return submissions;
                },
                cancellationToken);
        }
        catch (DuplicateKeyOnSaveException exception)
        {
            // The current-row read above has passed, so this is a concurrent create.
            throw new ValidationException(
            [
                new ValidationFailure(
                    SubmissionProperty(exception.DocumentIndex),
                    PomSubmissionValidationMessages.CurrentSubmissionExists)
            ]);
        }

        logger.UpsertedPomSubmissionBatch(
            submissions.Count,
            submissions.Count - currentIds.Count,
            currentIds.Count);
        return results;
    }

    // Trims every item, runs the single-submission field validator on each, and rejects a repeated key.
    // A failure on one item does not stop the check of the later items.
    private async Task<PreparedBatch> PrepareBatchAsync(
        List<PomSubmissionRequest> requests,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);

        var failures = new List<ValidationFailure>();
        if (requests.Count == 0)
        {
            failures.Add(new ValidationFailure("Submissions", PomSubmissionValidationMessages.SubmissionsRequired));
            return new PreparedBatch([], failures);
        }

        var trimmed = new List<PomSubmissionRequest?>(requests.Count);
        for (var index = 0; index < requests.Count; index++)
        {
            // JSON can still contain null. Nullable annotations are not enforced by the serializer.
            var request = PostedItem(requests, index);
            if (request is null)
            {
                trimmed.Add(null);
                failures.Add(new ValidationFailure(
                    SubmissionProperty(index),
                    PomSubmissionValidationMessages.SubmissionRequired));
                continue;
            }

            var prepared = request.Trimmed();
            trimmed.Add(prepared);
            var result = await validators.Request.ValidateAsync(prepared, cancellationToken);
            foreach (var error in result.Errors)
            {
                failures.Add(new ValidationFailure($"[{index}].{error.PropertyName}", error.ErrorMessage));
            }
        }

        AddDuplicateFailures(trimmed, failures);
        if (failures.Count > 0)
        {
            return new PreparedBatch([], failures);
        }

        return new PreparedBatch([.. trimmed.Select(request => request!)], failures);
    }

    // The list type is non-nullable. A posted null is still null at runtime.
    [return: MaybeNull]
    private static PomSubmissionRequest PostedItem(List<PomSubmissionRequest> requests, int index) =>
        requests[index];

    // Two items for one current-submission key cannot both be saved. Only a well-formed key is compared.
    private static void AddDuplicateFailures(
        List<PomSubmissionRequest?> requests,
        List<ValidationFailure> failures)
    {
        var seen = new Dictionary<PomSubmissionKey, int>();
        for (var index = 0; index < requests.Count; index++)
        {
            var request = requests[index];
            if (request is null || !CanLookUpCurrentSubmission(request))
            {
                continue;
            }

            var key = PomSubmissionKey.From(request);
            if (seen.TryGetValue(key, out var firstIndex))
            {
                failures.Add(new ValidationFailure(
                    SubmissionProperty(index),
                    PomSubmissionValidationMessages.SubmissionRepeated(firstIndex)));
            }
            else
            {
                seen.Add(key, index);
            }
        }
    }

    // The unique index allows one isCurrent submission for a key, so every current row is set to false
    // before any replacement or new submission is inserted. Both writes use the caller's transaction.
    // One update covers every amendment. One insert covers the creates and the amendments.
    // The caller then writes the outbox events on that same session.
    private async Task WriteBatchAsync(
        IMongoTransaction transaction,
        List<string> currentSubmissionIds,
        List<PomSubmissionModel> submissions,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken)
    {
        if (currentSubmissionIds.Count > 0)
        {
            var superseded = await documents.Repository.SupersedeCurrentsAsync(
                transaction.Session,
                currentSubmissionIds,
                updatedAtUtc,
                cancellationToken);

            if (superseded != currentSubmissionIds.Count)
            {
                throw new ValidationException(
                [
                    SubmissionFailure(PomSubmissionValidationMessages.UpsertBatchSubmissionChanged)
                ]);
            }
        }

        await documents.Repository.CreateManyAsync(transaction.Session, submissions, cancellationToken);
    }

    private static Dictionary<PomSubmissionKey, PomSubmissionModel> IndexCurrents(
        IReadOnlyList<PomSubmissionModel> currents)
    {
        var indexed = new Dictionary<PomSubmissionKey, PomSubmissionModel>(currents.Count);
        foreach (var current in currents)
        {
            indexed[PomSubmissionKey.From(current)] = current;
        }

        return indexed;
    }

    // A known bulk-insert position names that submission. A conflict we cannot place stays on Submission.
    private static string SubmissionProperty(int? index) =>
        index is >= 0 ? $"[{index}].Submission" : "Submission";

    private static string SubmissionProperty(int index) => $"[{index}].Submission";

    private sealed record PreparedBatch(
        IReadOnlyList<PomSubmissionRequest> Requests,
        List<ValidationFailure> Failures);
}
