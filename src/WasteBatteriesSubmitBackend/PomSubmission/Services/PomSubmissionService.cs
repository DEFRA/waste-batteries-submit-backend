using FluentValidation.Results;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using WasteBatteriesSubmitBackend.Config;
using WasteBatteriesSubmitBackend.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Mapping;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Requests;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Responses;
using WasteBatteriesSubmitBackend.PomSubmission.Repositories;
using WasteBatteriesSubmitBackend.PomSubmission.Validators;
using WasteBatteriesSubmitBackend.Utils.Mongo;
using WasteBatteriesSubmitBackend.Utils.Time;

namespace WasteBatteriesSubmitBackend.PomSubmission.Services;

public interface IPomSubmissionService
{
    Task<PomSubmissionModel> CreateAsync(PomSubmissionRequest request, CancellationToken cancellationToken = default);

    Task<PomSubmissionModel> AmendAsync(PomSubmissionRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PomSubmissionUpsertResult>> UpsertBatchAsync(
        List<PomSubmissionRequest> requests,
        CancellationToken cancellationToken = default);

    Task<PomSubmissionModel?> GetAsync(string id, CancellationToken cancellationToken = default);

    // gzipResponse selects the larger page limit. The list route sets it when the caller accepts gzip.
    Task<PaginatedList<PomSubmissionModel>> GetAsync(
        PomSubmissionFilterRequest request,
        bool gzipResponse,
        CancellationToken cancellationToken = default);
}

// Single creates and amends are in the Save partial. The batch upsert is in UpsertBatch.
// Reads are in the Query partial. These helpers are shared by the writes.
public partial class PomSubmissionService(
    IPomSubmissionRepository pomSubmissionRepository,
    IUnitOfWork unitOfWork,
    IPomSubmissionMapper pomSubmissionMapper,
    IOptions<PomSubmissionConfig> pomSubmissionConfig,
    PomSubmissionValidators validators,
    ITimeProvider timeProvider,
    ILogger<PomSubmissionService> logger) : IPomSubmissionService
{
    private static ValidationFailure SubmissionFailure(string message) =>
        new("Submission", message);

    // The current-submission key can be read only when these fields are well formed.
    private static bool CanLookUpCurrentSubmission(PomSubmissionRequest request) =>
        ObjectId.TryParse(request.SchemeOrgId, out _)
        && ObjectId.TryParse(request.MemberOrgId, out _)
        && CalendarYear.Contains(request.CompliancePeriodYear)
        && !string.IsNullOrWhiteSpace(request.CompliancePeriodQuarter);
}
