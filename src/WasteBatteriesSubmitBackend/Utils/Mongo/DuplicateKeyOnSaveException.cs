namespace WasteBatteriesSubmitBackend.Utils.Mongo;

// A save lost a race with a unique index. The caller decides what that conflict means.
// DocumentIndex is the position in a bulk insert. A single insert leaves it null.
public sealed class DuplicateKeyOnSaveException(Exception innerException, int? documentIndex = null)
    : Exception("A save conflicted with an existing unique key.", innerException)
{
    public int? DocumentIndex { get; } = documentIndex;
}
