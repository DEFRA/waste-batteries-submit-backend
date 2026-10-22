using Microsoft.Extensions.Options;
using MongoDB.Bson;
using WasteBatteriesSubmitBackend.Config;
using WasteBatteriesSubmitBackend.PomSubmission.Models;

namespace WasteBatteriesSubmitBackend.PomSubmission.Mapping;

public interface IPomSubmissionMapper
{
    // createdAtUtc comes from the caller so a batch shares one timestamp.
    // version replaces the configured initial version when an amendment allocates the next one.
    PomSubmissionModel ToNewSubmission(PomSubmissionRequest request, DateTime createdAtUtc, int? version = null);
}

public sealed class PomSubmissionMapper(IOptions<PomSubmissionConfig> options) : IPomSubmissionMapper
{
    public PomSubmissionModel ToNewSubmission(PomSubmissionRequest request, DateTime createdAtUtc, int? version = null)
    {
        var defaults = options.Value;
        var entries = (request.TonnageEntries ?? []).ConvertAll(
            entry => entry.ToStoredEntry(ObjectId.GenerateNewId().ToString(), defaults.InitialActivity));

        return new PomSubmissionModel(
            request,
            ObjectId.GenerateNewId().ToString(),
            createdAtUtc,
            entries,
            defaults.InitialStatus,
            version ?? defaults.InitialVersion);
    }
}
