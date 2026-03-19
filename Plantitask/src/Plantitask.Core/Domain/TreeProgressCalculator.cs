using Plantitask.Core.Constants;
using Plantitask.Core.Enums;

namespace Plantitask.Core.Domain;

public static class TreeProgressCalculator
{
    public static TreeStage CalculateStage(double completionPercentage) =>
        completionPercentage switch
        {
            TreeThresholds.NoSeedPlanted => TreeStage.EmptySoil,
            < TreeThresholds.SeedThreshold => TreeStage.Seed,
            < TreeThresholds.SproutThreshold => TreeStage.Sprout,
            < TreeThresholds.SaplingThreshold => TreeStage.Sapling,
            < TreeThresholds.YoungTreeThreshold => TreeStage.YoungTree,
            < TreeThresholds.FullTreeThreshold => TreeStage.FullTree,
            _ => TreeStage.FloweringTree
        };

    public static double CalculateCompletion(int totalTasks, int completedTasks) =>
        totalTasks == 0
            ? 0.0
            : Math.Round((double)completedTasks / totalTasks * 100, 1);
}