using System.Text.RegularExpressions;

namespace WasteBatteriesSubmitBackend.PomSubmission.Validators;

// Patterns for a posted POM submission. The BPRN pattern names the compliance year group "year".
public static partial class PomSubmissionPatterns
{
    public const string Tonnes = @"^\d+\.\d{3}$";

    public const string Bprn = @"^BPRN-(EA|NRW|SEPA|NIEA)-(?<year>\d{4})-\d{6}$";

    [GeneratedRegex(Tonnes, RegexOptions.CultureInvariant)]
    public static partial Regex TonnesPattern();

    [GeneratedRegex(Bprn, RegexOptions.CultureInvariant)]
    public static partial Regex BprnPattern();
}
