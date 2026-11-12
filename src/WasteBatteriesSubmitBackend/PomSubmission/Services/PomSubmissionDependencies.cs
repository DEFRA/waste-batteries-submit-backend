using Microsoft.Extensions.Options;
using WasteBatteriesSubmitBackend.Config;
using WasteBatteriesSubmitBackend.Outbox.Repositories;
using WasteBatteriesSubmitBackend.PomSubmission.Mapping;
using WasteBatteriesSubmitBackend.PomSubmission.Repositories;
using WasteBatteriesSubmitBackend.PomSubmission.Validators;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.PomSubmission.Services;

// The submission feature's own collaborators. Time, correlation, the caller and the logger stay on the service:
// those belong to the request, not to placed-on-market submissions.
public sealed class PomSubmissionDependencies(
    IPomSubmissionRepository repository,
    IOutboxRepository outbox,
    IUnitOfWork unitOfWork,
    IPomSubmissionMapper mapper,
    PomSubmissionValidators validators,
    IOptions<PomSubmissionConfig> config)
{
    public IPomSubmissionRepository Repository { get; } = repository;

    public IOutboxRepository Outbox { get; } = outbox;

    public IUnitOfWork UnitOfWork { get; } = unitOfWork;

    public IPomSubmissionMapper Mapper { get; } = mapper;

    public PomSubmissionValidators Validators { get; } = validators;

    public IOptions<PomSubmissionConfig> Config { get; } = config;
}
