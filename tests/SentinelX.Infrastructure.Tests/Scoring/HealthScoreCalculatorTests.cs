using SentinelX.Core.Models;
using SentinelX.Infrastructure.Scoring;

namespace SentinelX.Infrastructure.Tests.Scoring;

public class HealthScoreCalculatorTests
{
    private readonly HealthScoreCalculator _calculator = new();

    [Fact]
    public void NoFindings_ReturnsPerfectScore()
    {
        var score = _calculator.FromFindings(Array.Empty<Finding>());

        Assert.Equal(100, score);
    }

    [Fact]
    public void InfoFinding_DoesNotDeduct()
    {
        var findings = new[] { MakeFinding(Severity.Info) };

        var score = _calculator.FromFindings(findings);

        Assert.Equal(100, score);
    }

    [Fact]
    public void WarningFinding_Deducts15()
    {
        var findings = new[] { MakeFinding(Severity.Warning) };

        var score = _calculator.FromFindings(findings);

        Assert.Equal(85, score);
    }

    [Fact]
    public void CriticalFinding_Deducts40()
    {
        var findings = new[] { MakeFinding(Severity.Critical) };

        var score = _calculator.FromFindings(findings);

        Assert.Equal(60, score);
    }

    [Fact]
    public void MultipleCriticalFindings_FloorsAtZero()
    {
        var findings = new[]
        {
            MakeFinding(Severity.Critical),
            MakeFinding(Severity.Critical),
            MakeFinding(Severity.Critical)
        };

        var score = _calculator.FromFindings(findings);

        Assert.Equal(0, score);
    }

    [Fact]
    public void MixedSeverities_DeductsSumOfEach()
    {
        var findings = new[]
        {
            MakeFinding(Severity.Critical), // -40
            MakeFinding(Severity.Warning),  // -15
            MakeFinding(Severity.Info)      // -0
        };

        var score = _calculator.FromFindings(findings);

        Assert.Equal(45, score);
    }

    private static Finding MakeFinding(Severity severity) =>
        new("Title", "Explanation", severity, 1.0, "Action", "Impact");
}
