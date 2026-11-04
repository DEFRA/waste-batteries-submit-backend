using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using WasteBatteriesSubmitBackend.Config;
using WasteBatteriesSubmitBackend.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Mapping;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Queries;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Requests;
using WasteBatteriesSubmitBackend.PomSubmission.Repositories;
using WasteBatteriesSubmitBackend.PomSubmission.Validators;
using WasteBatteriesSubmitBackend.Utils.Logging;
using WasteBatteriesSubmitBackend.Utils.Mongo;
using WasteBatteriesSubmitBackend.Utils.Text;
using WasteBatteriesSubmitBackend.Utils.Time;

namespace WasteBatteriesSubmitBackend.PomSubmission.Services;

public interface IPomSubmissionService
{
    Task<PomSubmissionModel> CreateAsync(PomSubmissionRequest request, CancellationToken cancellationToken = default);

    Task<PomSubmissionModel> AmendAsync(PomSubmissionRequest request, CancellationToken cancellationToken = default);

    Task<PomSubmissionModel?> GetAsync(string id, CancellationToken cancellationToken = default);

    // gzipResponse selects the larger page limit. The list route sets it when the caller accepts gzip.
    Task<PaginatedList<PomSubmissionModel>> GetAsync(
        PomSubmissionListRequest request,
        bool gzipResponse,
        CancellationToken cancellationToken = default);
}

public class PomSubmissionService(
    IPomSubmissionRepository pomSubmissionRepository,
    IUnitOfWork unitOfWork,
    IPomSubmissionMapper pomSubmissionMapper,
    IOptions<PomSubmissionConfig> pomSubmissionConfig,
    PomSubmissionValidators validators,
    ITimeProvider timeProvider,
    ILogger<PomSubmissionService> logger) : IPomSubmissionService
{
    // Saves a first submission.
    // The request validator checks the posted shape only. A current submission for the same
    // scheme, member, year and quarter is read once: a first submission is rejected when one exists.
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

        var submission = pomSubmissionMapper.ToNewSubmission(prepared.Request, timeProvider.UtcNow);

        try
        {
            await pomSubmissionRepository.CreateAsync(submission, cancellationToken);
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

    // Saves a replacement for the current submission.
    // Field checks match create. The same current-submission read must find a row.
    // The replacement is a new submission with supersedesSubmissionId set to that row
    // and version set to one more than that row.
    // That row's isCurrent is cleared and its updatedAtUtc is set in the same transaction as the insert.
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

        var current = prepared.Current
            ?? throw new ValidationException(
            [
                SubmissionFailure(PomSubmissionValidationMessages.CurrentSubmissionMissing)
            ]);

        var amendedAtUtc = timeProvider.UtcNow;
        var submission = pomSubmissionMapper.ToNewSubmission(
            prepared.Request,
            amendedAtUtc,
            version: current.Version + 1,
            supersedesSubmissionId: current.Id);

        try
        {
            // Ids and the timestamp are allocated above. The transaction callback can run more than once.
            await unitOfWork.ExecuteAsync(
                async (transaction, transactionCancellationToken) =>
                {
                    await WriteAmendmentAsync(
                        transaction,
                        current.Id,
                        submission,
                        amendedAtUtc,
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

    // Returns the stored submission, or null when the id is not in the collection.
    public async Task<PomSubmissionModel?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        id = StringValues.TrimToEmpty(id);
        if (!ObjectId.TryParse(id, out _))
        {
            throw new ValidationException(
            [
                new ValidationFailure(nameof(PomSubmissionModel.Id), PomSubmissionValidationMessages.IdObjectId)
            ]);
        }

        var matches = await pomSubmissionRepository.FindAsync(
            new PomSubmissionQuery { Id = id, IsCurrent = null },
            new PageRequest(),
            cancellationToken);
        return matches.Items.Count == 0 ? null : matches.Items[0];
    }

    // Returns the submissions that match every supplied filter.
    // IsCurrent on the query defaults to true. A caller who sets it replaces that default.
    // A single submission, including one that is no longer current, is read by id on the document route.
    // A missing page size, or one above the maximum for this response, is limited to that maximum.
    // The maximum is the gzip page size when the response will be gzipped, and the plain page size otherwise.
    public async Task<PaginatedList<PomSubmissionModel>> GetAsync(
        PomSubmissionListRequest request,
        bool gzipResponse,
        CancellationToken cancellationToken = default)
    {
        request = request.Trimmed();
        await validators.List.ValidateAndThrowAsync(request, cancellationToken);

        var query = new PomSubmissionQuery
        {
            SchemeOrgId = request.SchemeOrgId,
            MemberOrgId = request.MemberOrgId,
            CompliancePeriodYears = Present(request.CompliancePeriodYears),
            CompliancePeriodQuarters = Present(request.CompliancePeriodQuarters)
        };

        if (request.IsCurrent is bool isCurrent)
        {
            query = query with { IsCurrent = isCurrent };
        }

        return await pomSubmissionRepository.FindAsync(
            query,
            new PageRequest
            {
                PageNumber = request.PageNumber,
                PageSize = LimitedPageSize(request.PageSize, gzipResponse)
            },
            cancellationToken);
    }

    private int LimitedPageSize(int? pageSize, bool gzipResponse)
    {
        var limits = pomSubmissionConfig.Value;
        var maximum = gzipResponse ? limits.MaxGzipPageSize : limits.MaxPageSize;
        return pageSize is null || pageSize.Value > maximum ? maximum : pageSize.Value;
    }

    private static IReadOnlyList<T>? Present<T>(IReadOnlyList<T>? values) =>
        values is { Count: > 0 } ? values : null;

    // Field checks, then one read of the current submission when the key is well formed.
    // Create and amend turn that read into a validation error. A batch upsert keeps the read
    // and chooses create or amend from Current, without either error.
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
            new PomSubmissionListRequest
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
    // A batch upsert can call this for each existing submission, inside one unit of work.
    // An outbox write can take the same session.
    private async Task WriteAmendmentAsync(
        IMongoTransaction transaction,
        string currentSubmissionId,
        PomSubmissionModel amendment,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken)
    {
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

    private static ValidationFailure SubmissionFailure(string message) =>
        new("Submission", message);

    // The current-submission key can be read only when these fields are well formed.
    private static bool CanLookUpCurrentSubmission(PomSubmissionRequest request) =>
        ObjectId.TryParse(request.SchemeOrgId, out _)
        && ObjectId.TryParse(request.MemberOrgId, out _)
        && CalendarYear.Contains(request.CompliancePeriodYear)
        && !string.IsNullOrWhiteSpace(request.CompliancePeriodQuarter);

    private sealed record PreparedSubmission(
        PomSubmissionRequest Request,
        List<ValidationFailure> Failures,
        PomSubmissionModel? Current);
}
