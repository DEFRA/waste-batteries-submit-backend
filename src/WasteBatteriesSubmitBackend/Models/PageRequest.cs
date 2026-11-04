namespace WasteBatteriesSubmitBackend.Models;

// Optional page. Omit PageSize to return every match. PageNumber then defaults to 1.
public sealed record PageRequest
{
    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }
}
