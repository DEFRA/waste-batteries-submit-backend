using Microsoft.Extensions.Options;
using MongoDB.Bson;
using WasteBatteriesSubmitBackend.Config;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.Utils.Time;

namespace WasteBatteriesSubmitBackend.PomSubmission.Mapping;

public interface IPomSubmissionMapper
{
    PomSubmissionModel ToNewSubmission(PomSubmissionRequest request);
}

public sealed class PomSubmissionMapper(ITimeProvider timeProvider, IOptions<PomSubmissionConfig> options) : IPomSubmissionMapper
{
    public PomSubmissionModel ToNewSubmission(PomSubmissionRequest request)
    {
        var defaults = options.Value;
        var createdAtUtc = timeProvider.UtcNow;
        var entries = (request.TonnageEntries ?? []).ConvertAll(
            entry => entry.ToStoredEntry(ObjectId.GenerateNewId().ToString(), defaults.InitialActivity));

        return new PomSubmissionModel(
            request,
            ObjectId.GenerateNewId().ToString(),
            createdAtUtc,
            entries,
            defaults.InitialStatus,
            defaults.InitialVersion);
    }
}
