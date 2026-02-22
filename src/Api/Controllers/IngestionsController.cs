using Api.Contracts;
using Application;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("v1/ingestions")]
public sealed class IngestionsController(IIngestionService ingestionService) : ControllerBase
{
    [HttpPost("")]
    public async Task<ActionResult<JobCreateResponse>> CreateIngestion(
        [FromBody] IngestionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        var errors = request.Validate();
        if (errors.Count > 0)
        {
            return ValidationProblem(errors);
        }

        var response = await ingestionService.SubmitAsync(
            new SubmitIngestionRequest(
                request.TenantId,
                request.Events.Select(x => new IngestionEventInput(x.Type, x.Timestamp, x.Payload)).ToList()),
            idempotencyKey,
            ct);

        return Accepted($"/v1/ingestions/{response.JobId}", new JobCreateResponse(response.JobId));
    }

    [HttpGet("{jobId:guid}")]
    public async Task<ActionResult<JobStatusResponse>> GetStatus(Guid jobId, CancellationToken ct)
    {
        var status = await ingestionService.GetStatusAsync(jobId, ct);
        if (status is null)
        {
            return NotFound(new ProblemDetails { Title = "Job not found", Status = 404 });
        }

        return Ok(new JobStatusResponse(status.JobId, status.Status, status.Attempt, status.CreatedAt, status.UpdatedAt, status.ProcessedAt, status.Error));
    }
}
