using Microsoft.AspNetCore.Mvc;
using APBD_PJATK_Cw6_s34002.DTOs;
using APBD_PJATK_Cw6_s34002.Exceptions;
using APBD_PJATK_Cw6_s34002.Services;

namespace APBD_PJATK_Cw6_s34002.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AppointmentsController(IAppointmentService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAppointments([FromQuery] string? status, [FromQuery] string? patientLastName, CancellationToken ct)
    {
        return Ok(await service.GetAppointmentsAsync(status, patientLastName, ct));
    }

    [HttpGet("{idAppointment:int}")]
    public async Task<IActionResult> GetAppointmentDetails(int idAppointment, CancellationToken ct)
    {
        try
        {
            return Ok(await service.GetAppointmentDetailsAsync(idAppointment, ct));
        }
        catch (NotFoundException e)
        {
            return NotFound(new ErrorResponseDto { Message = e.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateAppointment([FromBody] CreateAppointmentRequestDto request, CancellationToken ct)
    {
        if (request.AppointmentDate <= DateTime.Now)
        {
            return BadRequest(new ErrorResponseDto { Message = "Appointment date must be in the future." });
        }

        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 250)
        {
            return BadRequest(new ErrorResponseDto { Message = "Reason is required and cannot exceed 250 characters." });
        }

        try
        {
            var newId = await service.CreateAppointmentAsync(request, ct);
            return CreatedAtAction(nameof(GetAppointmentDetails), new { idAppointment = newId }, null);
        }
        catch (NotFoundException e)
        {
            return NotFound(new ErrorResponseDto { Message = e.Message });
        }
        catch (ConflictException e)
        {
            return Conflict(new ErrorResponseDto { Message = e.Message });
        }
    }

    [HttpPut("{idAppointment:int}")]
    public async Task<IActionResult> UpdateAppointment(int idAppointment, [FromBody] UpdateAppointmentRequestDto request, CancellationToken ct)
    {
        var validStatuses = new[] { "Scheduled", "Completed", "Cancelled" };
        if (!validStatuses.Contains(request.Status))
        {
            return BadRequest(new ErrorResponseDto { Message = "Invalid status." });
        }

        try
        {
            await service.UpdateAppointmentAsync(idAppointment, request, ct);
            return Ok();
        }
        catch (NotFoundException e)
        {
            return NotFound(new ErrorResponseDto { Message = e.Message });
        }
        catch (ConflictException e)
        {
            return Conflict(new ErrorResponseDto { Message = e.Message });
        }
    }

    [HttpDelete("{idAppointment:int}")]
    public async Task<IActionResult> DeleteAppointment(int idAppointment, CancellationToken ct)
    {
        try
        {
            await service.DeleteAppointmentAsync(idAppointment, ct);
            return NoContent();
        }
        catch (NotFoundException e)
        {
            return NotFound(new ErrorResponseDto { Message = e.Message });
        }
        catch (ConflictException e)
        {
            return Conflict(new ErrorResponseDto { Message = e.Message });
        }
    }
}
