using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using WasteBatteriesSubmitBackend.Config;
using WasteBatteriesSubmitBackend.Models;
using WasteBatteriesSubmitBackend.Outbox;
using WasteBatteriesSubmitBackend.Outbox.Repositories;
using WasteBatteriesSubmitBackend.PomSubmission.Mapping;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Requests;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Responses;
using WasteBatteriesSubmitBackend.PomSubmission.Repositories;
using WasteBatteriesSubmitBackend.PomSubmission.Validators;
using WasteBatteriesSubmitBackend.Utils.Actor;
using WasteBatteriesSubmitBackend.Utils.Correlation;
using WasteBatteriesSubmitBackend.Utils.Logging;
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
    IOutboxRepository outboxRepository,
    IUnitOfWork unitOfWork,
    IPomSubmissionMapper pomSubmissionMapper,
    IOptions<PomSubmissionConfig> pomSubmissionConfig,
    PomSubmissionValidators validators,
    ITimeProvider timeProvider,
    ICorrelationContext correlationContext,
    IActorContext actorContext,
    ILogger<PomSubmissionService> logger) : IPomSubmissionService
{
    private static ValidationFailure SubmissionFailure(string message) =>
        new("Submission", message);

    // One event per scheme and compliance year, in the order those pairs first appear.
    // Ids are allocated here so a transaction retry writes the same events.
    private IReadOnlyList<OutboxEvent> OutboxFor(
        IEnumerable<(PomSubmissionModel Submission, bool Amended)> saved,
        DateTime occurredAtUtc) =>
        OutboxEvents.ForSchemeYears(
            saved.Select(item => (item.Submission.SchemeOrgId, item.Submission.CompliancePeriodYear, item.Amended)),
            occurredAtUtc,
            correlationContext.CorrelationId,
            actorContext.ActorId);

    // Submission writes and outbox inserts share this transaction. The driver aborts it when work throws.
    // Validation and a duplicate key are the caller's response. Any other failure is logged and rethrown.
    private async Task<T> ExecuteSaveAsync<T>(
        Func<IMongoTransaction, CancellationToken, Task<T>> work,
        CancellationToken cancellationToken)
    {
        try
        {
            return await unitOfWork.ExecuteAsync(work, cancellationToken);
        }
        catch (Exception exception) when (exception is not ValidationException and not DuplicateKeyOnSaveException)
        {
            logger.FailedToSavePomSubmissions(exception);
            throw;
        }
    }

    // The current-submission key can be read only when these fields are well formed.
    private static bool CanLookUpCurrentSubmission(PomSubmissionRequest request) =>
        ObjectId.TryParse(request.SchemeOrgId, out _)
        && ObjectId.TryParse(request.MemberOrgId, out _)
        && CalendarYear.Contains(request.CompliancePeriodYear)
        && !string.IsNullOrWhiteSpace(request.CompliancePeriodQuarter);
}
