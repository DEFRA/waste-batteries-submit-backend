namespace WasteBatteriesSubmitBackend.Utils.Text;

// Posted text is trimmed once, before validation. The two results stay separate:
// a required string is never null, and an optional string uses null for every blank.
public static class StringValues
{
    public static string TrimToEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    public static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
