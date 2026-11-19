using Microsoft.Extensions.Options;
using WasteBatteriesSubmitBackend.Config;
using WasteBatteriesSubmitBackend.Outbox.Repositories;
using WasteBatteriesSubmitBackend.PomSubmission.Mapping;
using WasteBatteriesSubmitBackend.PomSubmission.Repositories;
using WasteBatteriesSubmitBackend.PomSubmission.Validators;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.PomSubmission.Services;

// A grouping of all the Pom Submission dependencies
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
