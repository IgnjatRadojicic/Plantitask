using Plantitask.Infrastructure.Services;

namespace Plantitask.Tests.Services
{
    public class GroupCodeGeneratorTests
    {
        private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        private readonly GroupCodeGenerator _sut = new();

        [Fact]
        public void Generate_ReturnsEightCharacters()
        {
            Assert.Equal(8, _sut.Generate().Length);
        }

        /// <summary>
        /// The alphabet drops the characters that get confused when a code is read aloud or off
        /// a screenshot. A generator that quietly widened its alphabet would still produce codes
        /// that look fine, so the exclusion is asserted by name.
        /// </summary>
        [Fact]
        public void Generate_NeverEmitsTheAmbiguousCharacters()
        {
            var thousandCodes = string.Concat(Enumerable.Range(0, 1000).Select(_ => _sut.Generate()));

            Assert.DoesNotContain('0', thousandCodes);
            Assert.DoesNotContain('O', thousandCodes);
            Assert.DoesNotContain('1', thousandCodes);
            Assert.DoesNotContain('I', thousandCodes);
            Assert.All(thousandCodes, c => Assert.Contains(c, Alphabet));
        }

        /// <summary>
        /// Uniqueness against the database is the caller's job, but a generator that repeated
        /// itself inside a thousand draws would make that job impossible. This is an entropy
        /// sanity check rather than a guarantee of no collisions.
        /// </summary>
        [Fact]
        public void Generate_DoesNotRepeatItselfAcrossAThousandDraws()
        {
            var codes = Enumerable.Range(0, 1000).Select(_ => _sut.Generate()).ToList();

            Assert.Equal(codes.Count, codes.Distinct().Count());
        }

        [Fact]
        public void Generate_AlwaysProducesSomethingIsValidAccepts()
        {
            Assert.All(
                Enumerable.Range(0, 500).Select(_ => _sut.Generate()),
                code => Assert.True(_sut.IsValid(code), code));
        }

        [Theory]
        [InlineData("ABCDEFGH")]
        [InlineData("23456789")]
        [InlineData("A2B3C4D5")]
        public void IsValid_AcceptsAWellFormedCode(string code)
        {
            Assert.True(_sut.IsValid(code));
        }

        [Theory]
        [InlineData("ABCDEFG")]
        [InlineData("ABCDEFGHI")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void IsValid_RejectsAnythingOfTheWrongShape(string? code)
        {
            Assert.False(_sut.IsValid(code!));
        }

        /// <summary>
        /// The characters left out of the alphabet have to be rejected on the way in as well.
        /// A code containing one of them cannot have been issued by us, so it is worth failing
        /// before the database is asked.
        /// </summary>
        [Theory]
        [InlineData("ABCDEFG0")]
        [InlineData("ABCDEFGO")]
        [InlineData("ABCDEFG1")]
        [InlineData("ABCDEFGI")]
        [InlineData("abcdefgh")]
        [InlineData("ABCD EFG")]
        public void IsValid_RejectsCharactersOutsideTheAlphabet(string code)
        {
            Assert.False(_sut.IsValid(code));
        }
    }
}
