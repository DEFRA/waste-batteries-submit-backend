using FluentValidation;
using FluentValidation.Results;
using MongoDB.Bson;
using WasteBatteriesSubmitBackend.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Filters;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Requests;
using WasteBatteriesSubmitBackend.PomSubmission.Validators;
using WasteBatteriesSubmitBackend.Utils.Text;

namespace WasteBatteriesSubmitBackend.PomSubmission.Services;

// Reads. The list is paged. Gzip on that response selects the larger page.
public partial class PomSubmissionService
{
    // Returns the stored submission, including one that is no longer current.
    // Returns null when the id is not in the collection.
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
            new PomSubmissionFilter { Id = id, IsCurrent = null },
            new PageRequest(),
            cancellationToken);
        return matches.Items.Count == 0 ? null : matches.Items[0];
    }

    // Returns the submissions that match every supplied filter.
    // returnCurrentOnly defaults to true, so the read stays on current submissions.
    // False does not set IsCurrent to false. It clears that criterion, so the query returns current and historical submissions.
    // If Scheme, Members, Years and quarters are supplied it is an "AND", returning a list of submissions.
    // A missing page size, or one above the maximum for this response, is limited to that maximum.
    // The maximum is the gzip page size when the response will be gzipped, and the plain page size otherwise.
    public async Task<PaginatedList<PomSubmissionModel>> GetAsync(
        PomSubmissionFilterRequest request,
        bool gzipResponse,
        CancellationToken cancellationToken = default)
    {
        request = request.Trimmed();
        await validators.Filter.ValidateAndThrowAsync(request, cancellationToken);

        var filter = new PomSubmissionFilter
        {
            SchemeOrgId = request.SchemeOrgId,
            MemberOrgId = request.MemberOrgId,
            CompliancePeriodYears = Present(request.CompliancePeriodYears),
            CompliancePeriodQuarters = Present(request.CompliancePeriodQuarters)
        };

        if (request.ReturnCurrentOnly == false)
        {
            filter = filter with { IsCurrent = null };
        }

        return await pomSubmissionRepository.FindAsync(
            filter,
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
}
