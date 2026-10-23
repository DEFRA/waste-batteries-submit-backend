namespace WasteBatteriesSubmitBackend.PomSubmission.Models;

// A posted compliance period year is a four-digit calendar year.
public static class CalendarYear
{
    public const int Minimum = 1000;

    public const int Maximum = 9999;

    public static bool Contains(int year) => year is >= Minimum and <= Maximum;
}
