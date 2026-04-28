using APBD_PJATK_Cw6_s34002.DTOs;

namespace APBD_PJATK_Cw6_s34002.Services;

public interface IAppointmentService
{
    Task<IEnumerable<AppointmentListDto>> GetAppointmentsAsync(string? status, string? patientLastName, CancellationToken ct = default);
    Task<AppointmentDetailsDto> GetAppointmentDetailsAsync(int id, CancellationToken ct = default);
    Task<int> CreateAppointmentAsync(CreateAppointmentRequestDto request, CancellationToken ct = default);
    Task UpdateAppointmentAsync(int id, UpdateAppointmentRequestDto request, CancellationToken ct = default);
    Task DeleteAppointmentAsync(int id, CancellationToken ct = default);
}
