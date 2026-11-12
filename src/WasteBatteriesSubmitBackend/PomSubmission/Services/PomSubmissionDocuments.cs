using WasteBatteriesSubmitBackend.Outbox.Repositories;
using WasteBatteriesSubmitBackend.PomSubmission.Mapping;
using WasteBatteriesSubmitBackend.PomSubmission.Repositories;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.PomSubmission.Services;

// The stored submission, the outbox event written with it, and the mapper that builds the row.
public sealed class PomSubmissionDocuments(
    IPomSubmissionRepository repository,
    IOutboxRepository outbox,
    IUnitOfWork unitOfWork,
    IPomSubmissionMapper mapper)
{
    public IPomSubmissionRepository Repository { get; } = repository;

    public IOutboxRepository Outbox { get; } = outbox;

    public IUnitOfWork UnitOfWork { get; } = unitOfWork;

    public IPomSubmissionMapper Mapper { get; } = mapper;
}
