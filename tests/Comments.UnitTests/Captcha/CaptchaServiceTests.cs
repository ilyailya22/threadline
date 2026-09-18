using Threadline.Comments.Application.Captcha;
using Threadline.Comments.Application.Common.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;

namespace Threadline.Comments.UnitTests.Captcha;

public sealed class CaptchaServiceTests
{
    private static readonly Guid ChallengeId = Guid.CreateVersion7();

    private readonly Mock<ICaptchaStore> _store = new();

    [Fact]
    public async Task A_correct_answer_passes_whatever_its_case_and_surrounding_whitespace()
    {
        _store.Setup(s => s.ConsumeAsync(ChallengeId, It.IsAny<CancellationToken>())).ReturnsAsync("AB3K7");

        (await CreateService().ValidateAsync(ChallengeId, "  ab3k7 ")).ShouldBeTrue();
    }

    [Fact]
    public async Task A_wrong_answer_fails()
    {
        _store.Setup(s => s.ConsumeAsync(ChallengeId, It.IsAny<CancellationToken>())).ReturnsAsync("AB3K7");

        (await CreateService().ValidateAsync(ChallengeId, "AB3K8")).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_answer_fails_and_still_burns_the_challenge(string? answer)
    {
        _store.Setup(s => s.ConsumeAsync(ChallengeId, It.IsAny<CancellationToken>())).ReturnsAsync("AB3K7");

        (await CreateService().ValidateAsync(ChallengeId, answer)).ShouldBeFalse();

        // One-shot means one-shot: a submission without an answer must not leave the image
        // available for another guess.
        _store.Verify(s => s.ConsumeAsync(ChallengeId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_unknown_or_already_used_challenge_fails()
    {
        _store.Setup(s => s.ConsumeAsync(ChallengeId, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        (await CreateService().ValidateAsync(ChallengeId, "AB3K7")).ShouldBeFalse();
    }

    [Fact]
    public async Task An_empty_challenge_id_is_rejected_without_touching_the_store()
    {
        (await CreateService().ValidateAsync(Guid.Empty, "AB3K7")).ShouldBeFalse();

        _store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_issued_challenge_is_stored_for_its_lifetime_and_expires_accordingly()
    {
        var now = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

        var challenge = await CreateService(now).IssueAsync();

        challenge.ExpiresAt.ShouldBe(now.Add(CaptchaService.Lifetime));
        _store.Verify(s => s.StoreAsync(challenge.Id, "AB3K7", CaptchaService.Lifetime, It.IsAny<CancellationToken>()));
    }

    private CaptchaService CreateService(DateTimeOffset? now = null)
    {
        var generator = new Mock<ICaptchaCodeGenerator>();
        generator.Setup(g => g.Generate()).Returns("AB3K7");

        var renderer = new Mock<ICaptchaImageRenderer>();
        renderer.Setup(r => r.Render(It.IsAny<string>())).Returns([0x89]);

        var clock = new Mock<IDateTimeProvider>();
        clock.Setup(c => c.UtcNow).Returns(now ?? DateTimeOffset.UtcNow);

        return new CaptchaService(
            generator.Object,
            renderer.Object,
            _store.Object,
            clock.Object,
            NullLogger<CaptchaService>.Instance);
    }
}
