using MongoDB.Bson;
using WasteBatteriesSubmitBackend.Utils.Text;

namespace WasteBatteriesSubmitBackend.Utils.Mongo;

// ObjectId text is canonical, so a different hex case is the same id after a later read.
internal static class ObjectIds
{
    public static string CanonicalOrOriginal(string? value)
    {
        var trimmed = StringValues.TrimToEmpty(value);
        return ObjectId.TryParse(trimmed, out var objectId) ? objectId.ToString() : trimmed;
    }

    public static string? CanonicalOrNull(string? value)
    {
        var trimmed = StringValues.TrimToNull(value);
        return trimmed is null ? null : CanonicalOrOriginal(trimmed);
    }
}
