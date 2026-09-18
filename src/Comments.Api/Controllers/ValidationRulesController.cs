using Threadline.Comments.Application.Comments.Queries.GetValidationRules;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Threadline.Comments.Api.Controllers;

/// <summary>Publishes the validation rules the server enforces; see <see cref="GetValidationRulesQuery"/>.</summary>
[ApiController]
[Route("api/validation-rules")]
[Produces("application/json")]
public sealed class ValidationRulesController(ISender sender) : ControllerBase
{
    [HttpGet]
    [OutputCache(Duration = 3600)]
    [ProducesResponseType<ValidationRulesDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ValidationRulesDto>> Get(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetValidationRulesQuery(), cancellationToken));
}
