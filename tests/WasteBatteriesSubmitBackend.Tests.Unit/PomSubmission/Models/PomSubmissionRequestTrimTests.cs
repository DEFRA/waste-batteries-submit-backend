using System.Text.Json;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Requests;

namespace WasteBatteriesSubmitBackend.Tests.Unit.PomSubmission.Models;

public class PomSubmissionRequestTrimTests
{
    [Fact]
    public void Trimmed_WhenStringsHaveSurroundingWhitespace_RemovesIt()
    {
        // Arrange
        var request = new PomSubmissionRequest
        {
            SchemeOrgId = "  674a1b2c3d4e5f6789012345  ",
            MemberOrgId = "\t674a1b2c3d4e5f6789012346\t",
            MemberName = "  Northwind Batteries Ltd  ",
            MemberBprn = "  BPRN-EA-2026-000123  ",
            SchemeMembershipRef = "  SCH-00417  ",
            CompliancePeriodYear = 2026,
            CompliancePeriodQuarter = "  Quarter 2  ",
            DataBasis = "  Actual  ",
            TonnageEntries =
            [
                new TonnageEntryBase
                {
                    Classification = new BatteryClassification
                    {
                        BatteryCategory = "  Portable  ",
                        Chemistry = "  Alkaline  ",
                        SubCategory = "  General use  ",
                        CellType = "  Primary  ",
                        EquipmentState = "  In equipment  "
                    },
                    Tonnes = "  12.500  "
                }
            ]
        };

        // Act
        var trimmed = request.Trimmed();

        // Assert
        Assert.Equal("674a1b2c3d4e5f6789012345", trimmed.SchemeOrgId);
        Assert.Equal("674a1b2c3d4e5f6789012346", trimmed.MemberOrgId);
        Assert.Equal("Northwind Batteries Ltd", trimmed.MemberName);
        Assert.Equal("BPRN-EA-2026-000123", trimmed.MemberBprn);
        Assert.Equal("SCH-00417", trimmed.SchemeMembershipRef);
        Assert.Equal(2026, trimmed.CompliancePeriodYear);
        Assert.Equal("Quarter 2", trimmed.CompliancePeriodQuarter);
        Assert.Equal("Actual", trimmed.DataBasis);
        var entry = Assert.Single(trimmed.TonnageEntries);
        Assert.Equal("12.500", entry.Tonnes);
        Assert.Equal("Portable", entry.Classification.BatteryCategory);
        Assert.Equal("Alkaline", entry.Classification.Chemistry);
        Assert.Equal("General use", entry.Classification.SubCategory);
        Assert.Equal("Primary", entry.Classification.CellType);
        Assert.Equal("In equipment", entry.Classification.EquipmentState);
    }

    [Fact]
    public void Trimmed_WhenOptionalTextIsMissing_LeavesItNull()
    {
        // Arrange
        var request = new PomSubmissionRequest
        {
            MemberName = null!,
            TonnageEntries =
            [
                new TonnageEntryBase
                {
                    Tonnes = null!,
                    Classification = new BatteryClassification
                    {
                        BatteryCategory = null!,
                        Chemistry = null!,
                        SubCategory = null,
                        CellType = null,
                        EquipmentState = null
                    }
                },
                null!,
                new TonnageEntryBase { Classification = null! }
            ]
        };

        // Act
        var trimmed = request.Trimmed();

        // Assert
        Assert.Equal(string.Empty, trimmed.MemberName);
        Assert.Equal(string.Empty, trimmed.TonnageEntries[0].Tonnes);
        Assert.Equal(string.Empty, trimmed.TonnageEntries[0].Classification.BatteryCategory);
        Assert.Equal(string.Empty, trimmed.TonnageEntries[0].Classification.Chemistry);
        Assert.Null(trimmed.TonnageEntries[0].Classification.SubCategory);
        Assert.Null(trimmed.TonnageEntries[0].Classification.CellType);
        Assert.Null(trimmed.TonnageEntries[0].Classification.EquipmentState);
        Assert.Null(trimmed.TonnageEntries[1]);
        Assert.Null(trimmed.TonnageEntries[2].Classification);
    }

    [Fact]
    public void Trimmed_WhenTonnageEntriesAreNull_LeavesThemNull()
    {
        // Arrange
        var request = new PomSubmissionRequest { TonnageEntries = null! };

        // Act
        var trimmed = request.Trimmed();

        // Assert
        Assert.Null(trimmed.TonnageEntries);
    }

    [Fact]
    public void Trimmed_WhenOptionalClassificationIsOmittedNullEmptyOrWhitespace_StoresNull()
    {
        // Arrange
        const string header = """
            {
              "tonnageEntries": [
                {
                  "classification": {
                    "batteryCategory": "Portable",
                    "chemistry": "Alkaline"
            """;
        string[] optionalText =
        [
            "",
            """
            ,
                    "subCategory": null,
                    "cellType": null,
                    "equipmentState": null
            """,
            """
            ,
                    "subCategory": "",
                    "cellType": "",
                    "equipmentState": ""
            """,
            """
            ,
                    "subCategory": "   ",
                    "cellType": " \t ",
                    "equipmentState": "  "
            """
        ];

        // Act
        var stored = optionalText.Select(text =>
        {
            var json = header + text + """
                  },
                  "tonnes": "1.000"
                }
              ]
            }
            """;
            var request = JsonSerializer.Deserialize<PomSubmissionRequest>(json, JsonSerializerOptions.Web);
            return request!.Trimmed().TonnageEntries[0].Classification;
        }).ToList();

        // Assert
        Assert.All(stored, classification =>
        {
            Assert.Null(classification.SubCategory);
            Assert.Null(classification.CellType);
            Assert.Null(classification.EquipmentState);
        });
        Assert.All(stored, classification => Assert.Equal(stored[0], classification));
    }
}
