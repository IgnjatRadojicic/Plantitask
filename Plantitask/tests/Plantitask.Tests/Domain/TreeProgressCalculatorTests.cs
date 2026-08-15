using Plantitask.Core.Domain;
using Plantitask.Core.Enums;

namespace Plantitask.Tests.Domain
{
    public class TreeProgressCalculatorTests
    {
        [Fact]
        public void CalculateCompletion_WithNoTasks_IsZeroRatherThanDividingByZero()
        {
            Assert.Equal(0.0, TreeProgressCalculator.CalculateCompletion(0, 0));
        }

        [Theory]
        [InlineData(4, 0, 0.0)]
        [InlineData(4, 1, 25.0)]
        [InlineData(4, 4, 100.0)]
        [InlineData(3, 1, 33.3)]
        [InlineData(3, 2, 66.7)]
        [InlineData(7, 2, 28.6)]
        public void CalculateCompletion_RoundsToOneDecimalPlace(int total, int completed, double expected)
        {
            Assert.Equal(expected, TreeProgressCalculator.CalculateCompletion(total, completed));
        }

        /// <summary>
        /// Every arm of the switch gets the value just below its upper bound and the bound
        /// itself, because that pair is the only thing that separates a correct comparison from
        /// one written with the wrong operator.
        /// </summary>
        [Theory]
        [InlineData(0.0, TreeStage.EmptySoil)]
        [InlineData(0.1, TreeStage.Seed)]
        [InlineData(19.9, TreeStage.Seed)]
        [InlineData(20.0, TreeStage.Sprout)]
        [InlineData(39.9, TreeStage.Sprout)]
        [InlineData(40.0, TreeStage.Sapling)]
        [InlineData(59.9, TreeStage.Sapling)]
        [InlineData(60.0, TreeStage.YoungTree)]
        [InlineData(79.9, TreeStage.YoungTree)]
        [InlineData(80.0, TreeStage.FullTree)]
        [InlineData(99.9, TreeStage.FullTree)]
        [InlineData(100.0, TreeStage.FloweringTree)]
        public void CalculateStage_MapsEachBandToItsStage(double completion, TreeStage expected)
        {
            Assert.Equal(expected, TreeProgressCalculator.CalculateStage(completion));
        }

        /// <summary>
        /// Zero is matched by an exact constant arm rather than a range, so it is the one input
        /// where a stage boundary and a stage value coincide. A completion of nothing has to be
        /// bare soil and not a planted seed.
        /// </summary>
        [Fact]
        public void CalculateStage_TreatsExactlyZeroAsBareSoil()
        {
            Assert.Equal(TreeStage.EmptySoil, TreeProgressCalculator.CalculateStage(0));
            Assert.NotEqual(TreeStage.EmptySoil, TreeProgressCalculator.CalculateStage(0.1));
        }

        [Theory]
        [InlineData(0, 0, TreeStage.EmptySoil)]
        [InlineData(10, 1, TreeStage.Seed)]
        [InlineData(10, 3, TreeStage.Sprout)]
        [InlineData(10, 5, TreeStage.Sapling)]
        [InlineData(10, 7, TreeStage.YoungTree)]
        [InlineData(10, 9, TreeStage.FullTree)]
        [InlineData(10, 10, TreeStage.FloweringTree)]
        public void TheTwoCalculationsComposeIntoAStageForRealTaskCounts(
            int total, int completed, TreeStage expected)
        {
            var completion = TreeProgressCalculator.CalculateCompletion(total, completed);

            Assert.Equal(expected, TreeProgressCalculator.CalculateStage(completion));
        }
    }
}
