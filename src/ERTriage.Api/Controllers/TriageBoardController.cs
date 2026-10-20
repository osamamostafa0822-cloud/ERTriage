using ERTriage.Application.Common;
using ERTriage.Application.Triage;
using ERTriage.Application.Triage.Dtos;
using ERTriage.Domain.Patients;
using Microsoft.AspNetCore.Mvc;

namespace ERTriage.Api.Controllers;

/// <summary>The prioritized queue (docs/03-features/triage-board.md). Read-only.</summary>
[ApiController]
[Route("api/triage-board")]
[Produces("application/json")]
[ProducesResponseType(typeof(GeneralResponse<object>), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(GeneralResponse<object>), StatusCodes.Status500InternalServerError)]
public sealed class TriageBoardController(ITriageBoardService boardService) : ControllerBase
{
    /// <summary>
    /// Returns one page of the board ordered by priority, then arrival time. Filters are repeated query parameters:
    /// <c>?status=Waiting&amp;status=InTreatment&amp;priority=1&amp;priority=2</c>. Without <c>status</c> the board shows
    /// Waiting and InTreatment (Q7). Out-of-range paging is rejected with 400, never clamped.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(GeneralResponse<PagedResult<TriageBoardItemDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<GeneralResponse<PagedResult<TriageBoardItemDto>>>> Get(
        [FromQuery(Name = "status")] PatientStatus[]? status,
        [FromQuery(Name = "priority")] int[]? priority,
        [FromQuery] int pageNumber = PaginationRequest.FirstPage,
        [FromQuery] int pageSize = PaginationRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var request = new TriageBoardRequest(status, priority, pageNumber, pageSize);
        var page = await boardService.GetBoardAsync(request, cancellationToken);

        return Ok(GeneralResponse<PagedResult<TriageBoardItemDto>>.Ok(page));
    }
}
