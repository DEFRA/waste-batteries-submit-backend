using FluentValidation;
using WasteBatteriesSubmitBackend.PomSubmission.Mapping;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Repositories;
using WasteBatteriesSubmitBackend.Utils.Logging;

namespace WasteBatteriesSubmitBackend.PomSubmission.Services;

public interface IPomSubmissionService
{
    Task<PomSubmissionModel> CreateAsync(PomSubmissionRequest request, CancellationToken cancellationToken = default);
}

// Saves the posted submission.
// TODO: Check vocabulary texts against the reference lists, and resolve scheme and member references.
// TODO: Infer compliancePeriodYear from the submission window.
// TODO: An amendment adds a new submission whose SupersedesSubmissionId is the previous id, and clears isCurrent
// and sets updatedAtUtc on that previous submission in one transaction.
public class PomSubmissionService(
    IPomSubmissionRepository pomSubmissionRepository,
    IPomSubmissionMapper pomSubmissionMapper,
    IValidator<PomSubmissionRequest> pomSubmissionRequestValidator,
    ILogger<PomSubmissionService> logger) : IPomSubmissionService
{
    public async Task<PomSubmissionModel> CreateAsync(PomSubmissionRequest request, CancellationToken cancellationToken = default)
    {
        await pomSubmissionRequestValidator.ValidateAndThrowAsync(request, cancellationToken);
        var submission = pomSubmissionMapper.ToNewSubmission(request);
        await pomSubmissionRepository.CreateAsync(submission, cancellationToken);
        logger.CreatedPomSubmission(submission.Id, submission.SchemeOrgId, submission.MemberOrgId);
        return submission;
    }
}
