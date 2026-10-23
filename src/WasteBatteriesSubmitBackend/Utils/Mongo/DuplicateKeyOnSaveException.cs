namespace WasteBatteriesSubmitBackend.Utils.Mongo;

// A save lost a race with a unique index. The caller decides what that conflict means.
public sealed class DuplicateKeyOnSaveException(Exception innerException)
    : Exception("A save conflicted with an existing unique key.", innerException);
