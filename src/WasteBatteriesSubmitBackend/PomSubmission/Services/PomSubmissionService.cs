using FluentValidation;
using FluentValidation.Results;
using MongoDB.Bson;
using WasteBatteriesSubmitBackend.PomSubmission.Mapping;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Repositories;
using WasteBatteriesSubmitBackend.Utils.Logging;
using WasteBatteriesSubmitBackend.Utils.Time;

namespace WasteBatteriesSubmitBackend.PomSubmission.Services;

public interface IPomSubmissionService
{
    Task<PomSubmissionModel> CreateAsync(PomSubmissionRequest request, CancellationToken cancellationToken = default);
}

public class PomSubmissionService(
    IPomSubmissionRepository pomSubmissionRepository,
    IPomSubmissionMapper pomSubmissionMapper,
    IValidator<PomSubmissionRequest> pomSubmissionRequestValidator,
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
            var current = await pomSubmissionRepository.FindCurrentAsync(
                request.SchemeOrgId,
                request.MemberOrgId,
                request.CompliancePeriodYear,
                request.CompliancePeriodQuarter,
                cancellationToken);

            if (current is not null)
            {
                failures.Add(new ValidationFailure(
                    "Submission",
                    "A current submission already exists for this scheme, member, year and quarter."));
            }
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        var submission = pomSubmissionMapper.ToNewSubmission(request, timeProvider.UtcNow);
        await pomSubmissionRepository.CreateAsync(submission, cancellationToken);
        logger.CreatedPomSubmission(submission.Id, submission.SchemeOrgId, submission.MemberOrgId);
        return submission;
    }

    // TODO: An amendment loads the current submission once, rejects when it is missing, writes a new
    // submission with SupersedesSubmissionId and the next version, and clears isCurrent and sets
    // updatedAtUtc on the previous submission in one transaction.

    // The current-submission key can be read only when these fields are well formed.
    private static bool CanLookUpCurrentSubmission(PomSubmissionRequest request) =>
        ObjectId.TryParse(request.SchemeOrgId, out _)
        && ObjectId.TryParse(request.MemberOrgId, out _)
        && request.CompliancePeriodYear is >= 1000 and <= 9999
        && !string.IsNullOrWhiteSpace(request.CompliancePeriodQuarter);
}
