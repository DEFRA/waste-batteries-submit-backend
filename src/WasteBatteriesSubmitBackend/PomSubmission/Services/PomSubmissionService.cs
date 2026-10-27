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

    Task<PomSubmissionModel?> GetAsync(string id, CancellationToken cancellationToken = default);

    // gzipResponse selects the larger page limit. The list route sets it when the caller accepts gzip.
    Task<PaginatedList<PomSubmissionModel>> GetAsync(
        PomSubmissionListRequest request,
        bool gzipResponse,
        CancellationToken cancellationToken = default);
}

public class PomSubmissionService(
    IPomSubmissionRepository pomSubmissionRepository,
    IPomSubmissionMapper pomSubmissionMapper,
    IOptions<PomSubmissionConfig> pomSubmissionConfig,
    IValidator<PomSubmissionRequest> pomSubmissionRequestValidator,
    IValidator<PomSubmissionListRequest> pomSubmissionListRequestValidator,
    ITimeProvider timeProvider,
    ILogger<PomSubmissionService> logger) : IPomSubmissionService
{
    // Saves a first submission.
    // The request validator checks the posted shape only. A current submission for the same
    // scheme, member, year and quarter is read once here: a first submission is rejected when one exists.
    // TODO: Check vocabulary texts against the reference lists, and resolve scheme and member references.
    // TODO: Infer compliancePeriodYear from the submission window.
    public async Task<PomSubmissionModel> CreateAsync(PomSubmissionRequest request, CancellationToken cancellationToken = default)
    {
        request = request.Trimmed();
        var result = await pomSubmissionRequestValidator.ValidateAsync(request, cancellationToken);
        var failures = new List<ValidationFailure>(result.Errors);

        if (CanLookUpCurrentSubmission(request))
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
            var current = matches.Items.Count == 0 ? null : matches.Items[0];

            if (current is not null)
            {
                failures.Add(new ValidationFailure(
                    "Submission",
                    PomSubmissionValidationMessages.CurrentSubmissionExists));
            }
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        var submission = pomSubmissionMapper.ToNewSubmission(request, timeProvider.UtcNow);

        try
        {
            await pomSubmissionRepository.CreateAsync(submission, cancellationToken);
        }
        catch (DuplicateKeyOnSaveException)
        {
            // The current-submission read above has passed, so this is the concurrent-create check.
            throw new ValidationException(
            [
                new ValidationFailure("Submission", PomSubmissionValidationMessages.CurrentSubmissionExists)
            ]);
        }

        logger.CreatedPomSubmission(submission.Id, submission.SchemeOrgId, submission.MemberOrgId);
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
        await pomSubmissionListRequestValidator.ValidateAndThrowAsync(request, cancellationToken);

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

    // TODO: An amendment loads the current submission once, rejects when it is missing, writes a new
    // submission with SupersedesSubmissionId and the next version, and clears isCurrent and sets
    // updatedAtUtc on the previous submission in one transaction.

    // The current-submission key can be read only when these fields are well formed.
    private static bool CanLookUpCurrentSubmission(PomSubmissionRequest request) =>
        ObjectId.TryParse(request.SchemeOrgId, out _)
        && ObjectId.TryParse(request.MemberOrgId, out _)
        && CalendarYear.Contains(request.CompliancePeriodYear)
        && !string.IsNullOrWhiteSpace(request.CompliancePeriodQuarter);
}
