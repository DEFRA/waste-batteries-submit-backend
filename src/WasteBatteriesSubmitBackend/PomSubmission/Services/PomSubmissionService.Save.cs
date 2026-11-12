using FluentValidation;
using FluentValidation.Results;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Requests;
using WasteBatteriesSubmitBackend.PomSubmission.Validators;
using WasteBatteriesSubmitBackend.Utils.Logging;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.PomSubmission.Services;

// One submission. Create rejects a current row. Amend requires one and replaces it.
// Each save also writes one outbox event for that scheme and compliance year, in the same transaction.
public partial class PomSubmissionService
{
    // Saves a first submission.
    // The request validator checks the posted shape only. A current submission for the same
    // scheme, member, year and quarter is read once: a first submission is rejected when one exists.
    // The submission and its outbox event commit together. Ids and the timestamp are chosen first
    // because the driver can run the transaction callback more than once.
    // TEMPORARY: Check vocabulary texts against the reference lists, and resolve scheme and member references.
    // TEMPORARY: Infer compliancePeriodYear from the submission window.
    public async Task<PomSubmissionModel> CreateAsync(PomSubmissionRequest request, CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(request, cancellationToken);
        if (prepared.Current is not null)
        {
            prepared.Failures.Add(SubmissionFailure(PomSubmissionValidationMessages.CurrentSubmissionExists));
        }

        if (prepared.Failures.Count > 0)
        {
            throw new ValidationException(prepared.Failures);
        }

        var createdAtUtc = timeProvider.UtcNow;
        var submission = pomSubmissionMapper.ToNewSubmission(prepared.Request, createdAtUtc);
        var outboxEvents = OutboxFor([(submission, false)], createdAtUtc);

        try
        {
            await ExecuteSaveAsync(
                async (transaction, transactionCancellationToken) =>
                {
                    await pomSubmissionRepository.CreateAsync(
                        transaction.Session,
                        submission,
                        transactionCancellationToken);
                    await outboxRepository.AddAsync(
                        transaction.Session,
                        outboxEvents,
                        transactionCancellationToken);
                    return submission;
                },
                cancellationToken);
        }
        catch (DuplicateKeyOnSaveException)
        {
            // The current-submission read above has passed, so this is the concurrent-create check.
            throw new ValidationException(
            [
                SubmissionFailure(PomSubmissionValidationMessages.CurrentSubmissionExists)
            ]);
        }

        logger.CreatedPomSubmission(submission.Id, submission.SchemeOrgId, submission.MemberOrgId);
        return submission;
    }

    // Saves a replacement for the submission.
    // The field checks match create.
    // The current-submission read (matching on scheme id, member id, year and quarter) must find an existing submission.
    // The replacement is a new submission with supersedesSubmissionId set to that existing/old submission
    // and version set to one more than that existing/old submission.
    // That existing/old submission's isCurrent is set to false and its updatedAtUtc is set in the same transaction as the new submission createdAtUtc.
    public async Task<PomSubmissionModel> AmendAsync(PomSubmissionRequest request, CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(request, cancellationToken);
        if (CanLookUpCurrentSubmission(prepared.Request) && prepared.Current is null)
        {
            prepared.Failures.Add(SubmissionFailure(PomSubmissionValidationMessages.CurrentSubmissionMissing));
        }

        if (prepared.Failures.Count > 0)
        {
            throw new ValidationException(prepared.Failures);
        }

        var current = prepared.Current!;

        var amendedAtUtc = timeProvider.UtcNow;
        var submission = pomSubmissionMapper.ToNewSubmission(
            prepared.Request,
            amendedAtUtc,
            version: current.Version + 1,
            supersedesSubmissionId: current.Id);
        var outboxEvents = OutboxFor([(submission, true)], amendedAtUtc);

        try
        {
            // Ids and the timestamp are allocated above. The transaction callback can run more than once.
            await ExecuteSaveAsync(
                async (transaction, transactionCancellationToken) =>
                {
                    await WriteAmendmentAsync(
                        transaction,
                        current.Id,
                        submission,
                        amendedAtUtc,
                        transactionCancellationToken);
                    await outboxRepository.AddAsync(
                        transaction.Session,
                        outboxEvents,
                        transactionCancellationToken);
                    return submission;
                },
                cancellationToken);
        }
        catch (DuplicateKeyOnSaveException)
        {
            throw new ValidationException(
            [
                SubmissionFailure(PomSubmissionValidationMessages.CurrentSubmissionChanged)
            ]);
        }

        logger.AmendedPomSubmission(submission.Id, current.Id, submission.SchemeOrgId, submission.MemberOrgId);
        return submission;
    }

    // Tidies up the request (e.g. trims the data), does field level validation check abnd gets the current submission using the submission key.
    // Returns the tidied request, any validation errors and the current submission if it exists
    private async Task<PreparedSubmission> PrepareAsync(
        PomSubmissionRequest request,
        CancellationToken cancellationToken)
    {
        request = request.Trimmed();
        var result = await validators.Request.ValidateAsync(request, cancellationToken);
        var failures = new List<ValidationFailure>(result.Errors);
        PomSubmissionModel? current = null;

        if (CanLookUpCurrentSubmission(request))
        {
            current = await FindCurrentAsync(request, cancellationToken);
        }

        return new PreparedSubmission(request, failures, current);
    }

    private async Task<PomSubmissionModel?> FindCurrentAsync(
        PomSubmissionRequest request,
        CancellationToken cancellationToken)
    {
        var matches = await GetAsync(
            new PomSubmissionFilterRequest
            {
                SchemeOrgId = request.SchemeOrgId,
                MemberOrgId = request.MemberOrgId,
                CompliancePeriodYears = [request.CompliancePeriodYear],
                CompliancePeriodQuarters = [request.CompliancePeriodQuarter]
            },
            gzipResponse: false,
            cancellationToken);
        return matches.Items.Count == 0 ? null : matches.Items[0];
    }

    // The unique index allows one current submission for the key, so the current row is cleared
    // before the replacement is inserted. Both writes use the caller's transaction.
    // The caller then writes the outbox event on that same session.
    private async Task WriteAmendmentAsync(
        IMongoTransaction transaction,
        string currentSubmissionId,
        PomSubmissionModel amendment,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken)
    {
        // Call this before inserting the replacement. The unique index allows one current submission for the key.
        // So we need to update the old/existing one (so isCurrent = false) before inserting the new amended submission.
        var superseded = await pomSubmissionRepository.SupersedeCurrentAsync(
            transaction.Session,
            currentSubmissionId,
            updatedAtUtc,
            cancellationToken);

        if (!superseded)
        {
            throw new ValidationException(
            [
                SubmissionFailure(PomSubmissionValidationMessages.CurrentSubmissionChanged)
            ]);
        }

        await pomSubmissionRepository.CreateAsync(transaction.Session, amendment, cancellationToken);
    }

    private sealed record PreparedSubmission(
        PomSubmissionRequest Request,
        List<ValidationFailure> Failures,
        PomSubmissionModel? Current);
}
