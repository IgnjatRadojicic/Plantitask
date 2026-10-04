using Plantitask.Core.Validation;

namespace Plantitask.Tests.Validation
{
    public class TimeZoneRulesTests
    {
        [Theory]
        [InlineData("Europe/Belgrade")]
        [InlineData("America/Argentina/Buenos_Aires")]
        [InlineData("UTC")]
        public void TryResolve_AcceptsIanaIds(string id)
        {
            Assert.True(TimeZoneRules.TryResolve(id, out var zone));
            Assert.Equal(id, zone.Id);
        }

        [Fact]
        public void TryResolve_ReturnsTheCanonicalIdWhateverTheInputCase()
        {
            Assert.True(TimeZoneRules.TryResolve("europe/belgrade", out var zone));
            Assert.Equal("Europe/Belgrade", zone.Id);
        }

        /// <summary>
        /// .NET resolves Windows ids on every platform but Postgres and the browser do not, so a
        /// Windows id stored on a group would break the backfill and the Web hint.
        /// </summary>
        [Fact]
        public void TryResolve_RejectsWindowsIds()
        {
            Assert.False(TimeZoneRules.TryResolve("Central Europe Standard Time", out var zone));
            Assert.Null(zone);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("Mars/Olympus")]
        public void TryResolve_RejectsMissingOrUnknownIds(string? id)
        {
            Assert.False(TimeZoneRules.TryResolve(id, out var zone));
            Assert.Null(zone);
        }
    }
}
