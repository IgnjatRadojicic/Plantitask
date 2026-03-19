// In Models/ or a Helpers/ folder
using Plantitask.Web.Models;

namespace Plantitask.Web.Helpers;

public static class KanbanTreeMapper
{
    public const int SeedlingStage = 0;
    public const int SaplingStage = 1;
    public const int GrowingStage = 2;
    public const int FullTreeStage = 3;

    private const int StatusNotStarted = 1;
    private const int StatusInProgress = 2;
    private const int StatusUnderReview = 3;
    private const int StatusCompleted = 4;

    private const double InProgressWeight = 0.33;
    private const double UnderReviewWeight = 0.66;
    private const double CompletedWeight = 1.0;

    public static int CalculateStage(List<KanbanColumnDto> columns)
    {
        var total = columns.Sum(c => c.Tasks.Count);
        if (total == 0) return SeedlingStage;

        var inProgress = columns.Where(c => c.StatusId == StatusInProgress).Sum(c => c.Tasks.Count);
        var underReview = columns.Where(c => c.StatusId == StatusUnderReview).Sum(c => c.Tasks.Count);
        var completed = columns.Where(c => c.StatusId == StatusCompleted).Sum(c => c.Tasks.Count);

        var weighted = (inProgress * InProgressWeight)
                     + (underReview * UnderReviewWeight)
                     + (completed * CompletedWeight);

        var pct = (weighted / total) * 100.0;

        return pct switch
        {
            < 25 => SeedlingStage,
            < 50 => SaplingStage,
            < 100 => GrowingStage,
            _ => FullTreeStage
        };
    }

    public static int MapFromBackendStage(int backendStage)
    {
        return backendStage switch
        {
            <= 1 => SeedlingStage,
            <= 3 => SaplingStage,
            <= 4 => GrowingStage,
            _ => FullTreeStage
        };
    }

    public static string GetImagePath(int stage) => $"images/trees/stage-{stage}.png";
}