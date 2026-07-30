using SentinelX.Core.Models;
using SentinelX.Infrastructure.Scoring;

namespace SentinelX.Infrastructure.Tests.Scoring;

public class ScoringServiceTests
{
    private readonly ScoringService _service = new();

    [Fact]
    public void NoCategories_ReturnsPerfectScore()
    {
        var score = _service.ComputeOverallScore(new Dictionary<EngineCategory, int>());

        Assert.Equal(100, score);
    }

    [Fact]
    public void SingleCategory_ReturnsThatCategoryScoreExactly()
    {
        // With only one registered category, renormalization must yield exactly its own score
        // regardless of that category's absolute weight in the weight table.
        var score = _service.ComputeOverallScore(new Dictionary<EngineCategory, int>
        {
            [EngineCategory.SplitCam] = 73
        });

        Assert.Equal(73, score);
    }

    [Fact]
    public void AllCategoriesPerfect_ReturnsPerfectScore()
    {
        var allCategories = Enum.GetValues<EngineCategory>()
            .ToDictionary(category => category, _ => 100);

        var score = _service.ComputeOverallScore(allCategories);

        Assert.Equal(100, score);
    }

    [Fact]
    public void HigherWeightCategory_PullsOverallScoreMoreThanLowerWeightCategory()
    {
        // Obs (weight 0.45) is weighted more heavily than SplitCam (weight 0.20), so a
        // zeroed Obs score should drag the overall average down further than an equally
        // zeroed SplitCam score, holding StreamValidation constant at a perfect 100.
        var obsHeavy = _service.ComputeOverallScore(new Dictionary<EngineCategory, int>
        {
            [EngineCategory.Obs] = 0,
            [EngineCategory.StreamValidation] = 100
        });

        var splitCamHeavy = _service.ComputeOverallScore(new Dictionary<EngineCategory, int>
        {
            [EngineCategory.SplitCam] = 0,
            [EngineCategory.StreamValidation] = 100
        });

        Assert.True(obsHeavy < splitCamHeavy);
    }

    [Fact]
    public void TwoCategories_WeightedAverage()
    {
        var score = _service.ComputeOverallScore(new Dictionary<EngineCategory, int>
        {
            [EngineCategory.SplitCam] = 40,
            [EngineCategory.StreamValidation] = 60
        });

        // (40*0.20 + 60*0.35) / (0.20+0.35) = 29 / 0.55 = 52.72... -> 53
        Assert.Equal(53, score);
    }
}
