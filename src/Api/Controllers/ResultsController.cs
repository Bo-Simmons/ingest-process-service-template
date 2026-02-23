using Api.Contracts;
using Application;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("v1/results")]
public sealed class ResultsController(IIngestionService ingestionService) : ControllerBase
{
    [HttpGet("{jobId:guid}")]
    public async Task<ActionResult<JobResultsResponse>> GetResults(Guid jobId, CancellationToken ct)
    {
        var results = await ingestionService.GetResultsAsync(jobId, ct);
        if (results is null)
        {
            return NotFound(new ProblemDetails { Title = "Job not found", Status = 404 });
        }

        var responseItems = results.Results
            .Select(static x => new Api.Contracts.ResultItem(x.EventType, x.Count))
            .ToList();

        return Ok(new JobResultsResponse(results.JobId, responseItems));
    }
}
