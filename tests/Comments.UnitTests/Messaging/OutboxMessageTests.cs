using Threadline.Comments.Domain.Comments;
using Threadline.Comments.Domain.Users;
using Threadline.Comments.Infrastructure.Messaging;
using Threadline.Comments.Infrastructure.Messaging.Contracts;
using Threadline.Comments.Infrastructure.Persistence.Outbox;
using Shouldly;

namespace Threadline.Comments.UnitTests.Messaging;

public sealed class OutboxMessageTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_domain_event_becomes_an_outbox_row_that_reads_back_as_its_contract()
    {
        var comment = Comment.CreateRoot(
            User.Register(UserName.Create("Anonym"), EmailAddress.Create("anonym@example.com"), null, Now),
            CommentBody.FromSanitized("Hi", "Hi"),
            ClientFingerprint.Create(new string('a', 64), userAgent: null, clientId: null),
            Now);
        var domainEvent = comment.DomainEvents.Single();

        var message = OutboxMessage.For(domainEvent.EventId, IntegrationEvents.From(domainEvent)!, domainEvent.OccurredAt);

        var payload = message.ReadPayload(IntegrationEvents.ContractFor(message.Type));

        var published = payload.ShouldBeOfType<CommentCreatedIntegrationEvent>();
        published.CommentId.ShouldBe(comment.Id);
        published.IsTopLevel.ShouldBeTrue();
        message.Id.ShouldBe(domainEvent.EventId);
    }

    [Fact]
    public void Failed_attempts_back_off_exponentially_up_to_a_ceiling()
    {
        var message = OutboxMessage.For(Guid.CreateVersion7(), new AttachmentReadyIntegrationEvent
        {
            CommentId = Guid.CreateVersion7(),
            AttachmentId = Guid.CreateVersion7(),
        }, Now);

        var delays = new List<TimeSpan>();

        for (var attempt = 0; attempt < 20; attempt++)
        {
            message.MarkFailed("broker unavailable", Now);
            delays.Add(message.NextAttemptAt!.Value - Now);
        }

        delays.Take(4).ShouldBe([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)]);
        delays.Max().ShouldBe(OutboxMessage.MaxBackoff);
        message.Attempts.ShouldBe(20);
        message.ProcessedAt.ShouldBeNull();
    }

    [Fact]
    public void A_long_error_is_truncated_to_fit_its_column()
    {
        var message = OutboxMessage.For(Guid.CreateVersion7(), new AttachmentReadyIntegrationEvent
        {
            CommentId = Guid.CreateVersion7(),
            AttachmentId = Guid.CreateVersion7(),
        }, Now);

        message.MarkFailed(new string('x', OutboxMessage.MaxErrorLength * 2), Now);

        message.Error!.Length.ShouldBe(OutboxMessage.MaxErrorLength);
    }

    [Fact]
    public void Publishing_clears_the_last_error()
    {
        var message = OutboxMessage.For(Guid.CreateVersion7(), new AttachmentReadyIntegrationEvent
        {
            CommentId = Guid.CreateVersion7(),
            AttachmentId = Guid.CreateVersion7(),
        }, Now);

        message.MarkFailed("transient", Now);
        message.MarkPublished(Now.AddSeconds(1));

        message.ProcessedAt.ShouldBe(Now.AddSeconds(1));
        message.Error.ShouldBeNull();
    }

    [Fact]
    public void An_unknown_contract_name_is_refused() =>
        Should.Throw<NotSupportedException>(() => IntegrationEvents.ContractFor("SomethingElse"));
}
