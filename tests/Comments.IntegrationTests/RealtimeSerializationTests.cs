using System.Text.Json;
using Threadline.Comments.Application.Comments.Dtos;
using Threadline.Comments.Domain.Comments;
using Threadline.Comments.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Threadline.Comments.IntegrationTests;

/// <summary>
/// The push channel and the request channel describe the same objects, so they have to spell them
/// the same way.
/// </summary>
/// <remarks>
/// SignalR serialises with its own options, not the MVC ones. Left alone it writes enums as
/// ordinals, so an attachment pushed to the browser arrived as <c>"kind": 1</c> while the very same
/// attachment fetched over REST arrived as <c>"kind": "Image"</c> — and a client that switches on
/// the name rendered a photograph with the icon it uses for text files. The bug is invisible in any
/// test that only speaks REST, which is why this one reaches for the hub's serializer directly.
/// </remarks>
[Collection(IntegrationTestSuite.Name)]
public sealed class RealtimeSerializationTests(CommentsApiFactory factory)
{
    [Fact]
    public void Hub_payloads_spell_enums_the_same_way_as_rest_payloads()
    {
        var hubJson = factory.Services
            .GetRequiredService<IOptions<JsonHubProtocolOptions>>()
            .Value.PayloadSerializerOptions;

        var attachment = new AttachmentDto(
            Guid.CreateVersion7(),
            AttachmentKind.Image,
            AttachmentStatus.Ready,
            "image/png",
            "cat.jpg",
            83524,
            "/api/attachments/x/content",
            "/api/attachments/x/thumbnail",
            180,
            240);

        var payload = JsonSerializer.Serialize(attachment, hubJson);

        payload.ShouldContain("\"kind\":\"Image\"");
        payload.ShouldContain("\"status\":\"Ready\"");
    }
}
