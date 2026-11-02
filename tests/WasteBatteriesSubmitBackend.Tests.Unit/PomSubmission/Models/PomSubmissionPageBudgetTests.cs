using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.Tests.Common;

namespace WasteBatteriesSubmitBackend.Tests.Unit.PomSubmission.Models;

public class PomSubmissionPageBudgetTests
{
    private const int LargestFittingPage = 772;

    // RoundedPageSize and RoundedGzipPageSize here should match the values in the app settings (MaxPageSize and MaxGzipPageSize) - if the values are out of sync then the tests fail
    // This is the number of standard json submission models that should be returned to ensure that it is within the payload limit
    private const int RoundedPageSize = 750;

    // This is the number of submission models when using gzip, that should be returned to ensure that it is within the payload limit
    private const int RoundedGzipPageSize = 17000;

    // Room above the configured page sizes. If the model gets smaller, these larger pages can fit and the limits can move up.
    private const int ExtraStandardResponses = 50;
    private const int ExtraGzipResponses = 1000;

    //Assume that it is 10MB as the limit for the payloads
    private const int CdpPayloadLimitBytes = 10 * 1024 * 1024;

    [Fact]
    public void MaxPageSize_StaysUnderTheUncompressedCdpPayloadLimit()
    {
        // Arrange
        var submission = FullSubmission();
        var configured = AppSettings.PomSubmission.MaxPageSize;

        // Act
        var fitted = LargestPageUnderLimit(submission);
        var atConfigured = PageBytes(submission, configured);

        // Assert
        Assert.Equal(LargestFittingPage, fitted);
        Assert.Equal(RoundedPageSize, configured);
        Assert.True(
            atConfigured < CdpPayloadLimitBytes,
            $"A page of {configured} submissions is {atConfigured} bytes, which is over the {CdpPayloadLimitBytes} byte limit.");
    }

    [Fact]
    public void MaxGzipPageSize_StaysUnderTheCdpPayloadLimit()
    {
        // Arrange
        var configured = AppSettings.PomSubmission.MaxGzipPageSize;
        var items = MeasuredPage(configured);

        // Act
        var gzipBytes = MeasuredPomSubmission.GzipPage(items, configured, pageNumber: 1, pageSize: configured);

        // Assert
        Assert.Equal(RoundedGzipPageSize, configured);
        Assert.True(
            gzipBytes < CdpPayloadLimitBytes,
            $"Gzip of {configured} submissions is {gzipBytes} bytes, which is over the {CdpPayloadLimitBytes} byte limit.");
    }

    [Fact]
    public void MaxPageSize_PlusExtraStandardResponses_ExceedsTheUncompressedCdpPayloadLimit()
    {
        // Arrange
        var submission = FullSubmission();
        var overLimitCount = AppSettings.PomSubmission.MaxPageSize + ExtraStandardResponses;

        // Act
        var bytes = PageBytes(submission, overLimitCount);

        // Assert
        Assert.True(
            bytes >= CdpPayloadLimitBytes,
            $"A page of {overLimitCount} submissions is {bytes} bytes, which is under the {CdpPayloadLimitBytes} byte limit. Raise MaxPageSize.");
    }

    [Fact]
    public void MaxGzipPageSize_PlusExtraGzipResponses_ExceedsTheCdpPayloadLimit()
    {
        // Arrange
        var overLimitCount = AppSettings.PomSubmission.MaxGzipPageSize + ExtraGzipResponses;
        var items = MeasuredPage(overLimitCount);

        // Act
        var gzipBytes = MeasuredPomSubmission.GzipPage(items, overLimitCount, pageNumber: 1, pageSize: overLimitCount);

        // Assert
        Assert.True(
            gzipBytes >= CdpPayloadLimitBytes,
            $"Gzip of {overLimitCount} submissions is {gzipBytes} bytes, which is under the {CdpPayloadLimitBytes} byte limit. Raise MaxGzipPageSize.");
    }

    private static PomSubmissionModel[] MeasuredPage(int count) =>
        MeasuredPomSubmission.Page(
            MeasuredPomSubmission.SchemeOrgId,
            count,
            AppSettings.PomSubmission.InitialActivity,
            AppSettings.PomSubmission.InitialStatus,
            AppSettings.PomSubmission.InitialVersion);

    private static PomSubmissionModel FullSubmission() =>
        MeasuredPomSubmission.Full(
            AppSettings.PomSubmission.InitialActivity,
            AppSettings.PomSubmission.InitialStatus,
            AppSettings.PomSubmission.InitialVersion);

    private static int LargestPageUnderLimit(PomSubmissionModel submission)
    {
        var one = PageBytes(submission, 1);
        var marginal = PageBytes(submission, 2) - one;
        var count = 1;
        while (one + count * marginal < CdpPayloadLimitBytes)
        {
            count++;
        }

        return count;
    }

    private static int PageBytes(PomSubmissionModel submission, int count)
    {
        var items = Enumerable.Repeat(submission, count).ToArray();
        return MeasuredPomSubmission.PageBytes(items, count, 1, count);
    }
}
