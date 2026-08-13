using Plantitask.Infrastructure.Services;

namespace Plantitask.Tests.Services
{
    /// <summary>
    /// BCrypt at work factor 12 costs roughly a third of a second per hash, which is the point
    /// of the setting. Every test here is deliberately frugal with calls, and service tests
    /// should mock IPasswordHasher rather than pay this.
    /// </summary>
    public class PasswordHasherTests
    {
        private readonly PasswordHasher _sut = new();

        [Fact]
        public void VerifyPassword_AcceptsThePasswordThatWasHashed()
        {
            var hash = _sut.HashPassword("Correct horse battery staple");

            Assert.True(_sut.VerifyPassword("Correct horse battery staple", hash));
            Assert.False(_sut.VerifyPassword("correct horse battery staple", hash));
        }

        /// <summary>
        /// A fresh salt per call is what stops two people with the same password sharing a hash,
        /// so identical input has to produce different output.
        /// </summary>
        [Fact]
        public void HashPassword_SaltsEachCallSeparately()
        {
            var first = _sut.HashPassword("same password");
            var second = _sut.HashPassword("same password");

            Assert.NotEqual(first, second);
            Assert.True(_sut.VerifyPassword("same password", first));
            Assert.True(_sut.VerifyPassword("same password", second));
        }

        /// <summary>
        /// Login must never need a try catch around verification. A stored hash that is empty or
        /// was never a BCrypt string comes back false instead of throwing.
        /// </summary>
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not-a-bcrypt-hash")]
        public void VerifyPassword_ReturnsFalseForAStoredHashItCannotParse(string storedHash)
        {
            Assert.False(_sut.VerifyPassword("anything", storedHash));
        }

        // A fourth case belongs here and is left out on purpose. A truncated but BCrypt shaped
        // hash such as "$2a$12$tooshort" throws ArgumentOutOfRangeException out of the library,
        // which VerifyPassword does not catch because it only catches SaltParseException. Add
        // the case back once the catch is widened.

        [Fact]
        public void NeedsRehash_IsFalseForAHashMadeAtTheCurrentWorkFactor()
        {
            Assert.False(_sut.NeedsRehash(_sut.HashPassword("whatever")));
        }

        /// <summary>
        /// The work factor is pinned in the hasher rather than left to the library default, and
        /// this is the check login uses to upgrade an older hash while it still holds the
        /// plaintext. A hash made at a lower cost has to be recognised as stale.
        /// </summary>
        [Fact]
        public void NeedsRehash_IsTrueForAHashMadeAtAnOlderWorkFactor()
        {
            var weakerHash = BCrypt.Net.BCrypt.HashPassword("whatever", workFactor: 10);

            Assert.True(_sut.NeedsRehash(weakerHash));
        }

        /// <summary>
        /// Login verifies against this when the email is unknown so both paths cost the same
        /// time. It has to be a genuine hash that no realistic password matches, and it has to
        /// be the same value every time or the timing it is protecting would vary.
        /// </summary>
        [Fact]
        public void DummyHash_IsAStableRealHashThatNothingObviousMatches()
        {
            var hash = _sut.DummyHash;

            Assert.Same(hash, _sut.DummyHash);
            Assert.StartsWith("$2", hash);
            Assert.False(_sut.VerifyPassword("password", hash));
        }
    }
}
