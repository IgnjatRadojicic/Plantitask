using Plantitask.Core.Domain;

namespace Plantitask.Tests.Domain
{
    public class TimeZoneMathTests
    {
        private static readonly TimeZoneInfo Belgrade = TimeZoneInfo.FindSystemTimeZoneById("Europe/Belgrade");
        private static readonly TimeZoneInfo Santiago = TimeZoneInfo.FindSystemTimeZoneById("America/Santiago");

        /// <summary>
        /// Europe leaves summer time on 2026-10-25 so the midnight that starts that day is still
        /// UTC+2 and the one that starts the next day is already UTC+1. A fixed offset would get
        /// one of the two wrong.
        /// </summary>
        [Theory]
        [InlineData("2026-10-25", "2026-10-24T22:00:00")]
        [InlineData("2026-10-26", "2026-10-25T23:00:00")]
        [InlineData("2027-03-28", "2027-03-27T23:00:00")]
        [InlineData("2027-03-29", "2027-03-28T22:00:00")]
        public void LocalMidnightToUtc_FollowsTheOffsetInForceOnThatDate(string date, string expectedUtc)
        {
            var result = TimeZoneMath.LocalMidnightToUtc(DateOnly.Parse(date), Belgrade);

            Assert.Equal(DateTime.Parse(expectedUtc), result);
        }

        [Fact]
        public void LocalMidnightToUtc_ReturnsUtcKind()
        {
            var result = TimeZoneMath.LocalMidnightToUtc(new DateOnly(2026, 10, 6), Belgrade);

            Assert.Equal(DateTimeKind.Utc, result.Kind);
        }

        /// <summary>
        /// Chile moves its clocks from 23:59 straight to 01:00 on the first Sunday of September.
        /// The precondition proves the zone data really skips that midnight so the test cannot
        /// pass by accident on a machine whose rules differ.
        /// </summary>
        [Fact]
        public void LocalMidnightToUtc_WhenMidnightIsSkipped_UsesTheFirstMomentOfTheDay()
        {
            var skippedDay = new DateOnly(2026, 9, 6);
            Assert.True(Santiago.IsInvalidTime(skippedDay.ToDateTime(TimeOnly.MinValue)));

            var result = TimeZoneMath.LocalMidnightToUtc(skippedDay, Santiago);

            Assert.Equal(new DateTime(2026, 9, 6, 4, 0, 0), result);
        }
    }
}
