using Microsoft.Data.SqlClient;
using APBD_PJATK_Cw6_s34002.DTOs;
using APBD_PJATK_Cw6_s34002.Exceptions;
using System.Data;

namespace APBD_PJATK_Cw6_s34002.Services;

public class AppointmentService(IConfiguration configuration) : IAppointmentService
{
    private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection") 
                                                ?? throw new InvalidOperationException("Connection string not found.");

    public async Task<IEnumerable<AppointmentListDto>> GetAppointmentsAsync(string? status, string? patientLastName, CancellationToken ct)
    {
        var appointments = new List<AppointmentListDto>();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = new SqlCommand("""
            SELECT
                a.IdAppointment,
                a.AppointmentDate,
                a.Status,
                a.Reason,
                p.FirstName + ' ' + p.LastName AS PatientFullName,
                p.Email AS PatientEmail
            FROM dbo.Appointments a
            JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
            WHERE (@Status IS NULL OR a.Status = @Status)
              AND (@PatientLastName IS NULL OR p.LastName = @PatientLastName)
            ORDER BY a.AppointmentDate;
            """, connection);

        command.Parameters.AddWithValue("@Status", string.IsNullOrEmpty(status) ? DBNull.Value : status);
        command.Parameters.AddWithValue("@PatientLastName", string.IsNullOrEmpty(patientLastName) ? DBNull.Value : patientLastName);

        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            appointments.Add(new AppointmentListDto
            {
                IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
                AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                Reason = reader.GetString(reader.GetOrdinal("Reason")),
                PatientFullName = reader.GetString(reader.GetOrdinal("PatientFullName")),
                PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail"))
            });
        }

        return appointments;
    }

    public async Task<AppointmentDetailsDto> GetAppointmentDetailsAsync(int id, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = new SqlCommand("""
            SELECT
                a.IdAppointment,
                a.AppointmentDate,
                a.Status,
                a.Reason,
                a.InternalNotes,
                a.CreatedAt,
                p.IdPatient,
                p.FirstName + ' ' + p.LastName AS PatientFullName,
                p.Email AS PatientEmail,
                p.PhoneNumber AS PatientPhoneNumber,
                d.IdDoctor,
                d.FirstName + ' ' + d.LastName AS DoctorFullName,
                d.LicenseNumber AS DoctorLicenseNumber,
                s.Name AS SpecializationName
            FROM dbo.Appointments a
            JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
            JOIN dbo.Doctors d ON d.IdDoctor = a.IdDoctor
            JOIN dbo.Specializations s ON s.IdSpecialization = d.IdSpecialization
            WHERE a.IdAppointment = @IdAppointment;
            """, connection);

        command.Parameters.AddWithValue("@IdAppointment", id);

        await using var reader = await command.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct))
        {
            throw new NotFoundException($"Appointment with ID {id} not found.");
        }

        return new AppointmentDetailsDto
        {
            IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
            AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
            Status = reader.GetString(reader.GetOrdinal("Status")),
            Reason = reader.GetString(reader.GetOrdinal("Reason")),
            InternalNotes = reader.IsDBNull(reader.GetOrdinal("InternalNotes")) ? string.Empty : reader.GetString(reader.GetOrdinal("InternalNotes")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            IdPatient = reader.GetInt32(reader.GetOrdinal("IdPatient")),
            PatientFullName = reader.GetString(reader.GetOrdinal("PatientFullName")),
            PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail")),
            PatientPhoneNumber = reader.GetString(reader.GetOrdinal("PatientPhoneNumber")),
            IdDoctor = reader.GetInt32(reader.GetOrdinal("IdDoctor")),
            DoctorFullName = reader.GetString(reader.GetOrdinal("DoctorFullName")),
            DoctorLicenseNumber = reader.GetString(reader.GetOrdinal("DoctorLicenseNumber")),
            SpecializationName = reader.GetString(reader.GetOrdinal("SpecializationName"))
        };
    }

    public async Task<int> CreateAppointmentAsync(CreateAppointmentRequestDto request, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        
        await using var checkPatientCmd = new SqlCommand("SELECT IsActive FROM dbo.Patients WHERE IdPatient = @IdPatient", connection);
        checkPatientCmd.Parameters.AddWithValue("@IdPatient", request.IdPatient);
        var patientActiveObj = await checkPatientCmd.ExecuteScalarAsync(ct);
        if (patientActiveObj == null) throw new NotFoundException("Patient not found.");
        if (!(bool)patientActiveObj) throw new ConflictException("Patient is not active.");

        await using var checkDoctorCmd = new SqlCommand("SELECT IsActive FROM dbo.Doctors WHERE IdDoctor = @IdDoctor", connection);
        checkDoctorCmd.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
        var doctorActiveObj = await checkDoctorCmd.ExecuteScalarAsync(ct);
        if (doctorActiveObj == null) throw new NotFoundException("Doctor not found.");
        if (!(bool)doctorActiveObj) throw new ConflictException("Doctor is not active.");

        await using var checkConflictCmd = new SqlCommand(
            "SELECT COUNT(1) FROM dbo.Appointments WHERE IdDoctor = @IdDoctor AND AppointmentDate = @AppointmentDate", connection);
        checkConflictCmd.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
        checkConflictCmd.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);
        var conflictCount = (int)(await checkConflictCmd.ExecuteScalarAsync(ct) ?? 0);
        if (conflictCount > 0)
        {
            throw new ConflictException("Doctor already has an appointment at this time.");
        }

        await using var insertCmd = new SqlCommand("""
            INSERT INTO dbo.Appointments (IdPatient, IdDoctor, AppointmentDate, Status, Reason, CreatedAt)
            VALUES (@IdPatient, @IdDoctor, @AppointmentDate, 'Scheduled', @Reason, GETDATE());
            SELECT SCOPE_IDENTITY();
            """, connection);
        insertCmd.Parameters.AddWithValue("@IdPatient", request.IdPatient);
        insertCmd.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
        insertCmd.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);
        insertCmd.Parameters.AddWithValue("@Reason", request.Reason);

        var idObj = await insertCmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(idObj);
    }

    public async Task UpdateAppointmentAsync(int id, UpdateAppointmentRequestDto request, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var getAppCmd = new SqlCommand("SELECT Status, AppointmentDate FROM dbo.Appointments WHERE IdAppointment = @Id", connection);
        getAppCmd.Parameters.AddWithValue("@Id", id);
        await using var reader = await getAppCmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            throw new NotFoundException($"Appointment with ID {id} not found.");
        }
        var currentStatus = reader.GetString(0);
        var currentDate = reader.GetDateTime(1);
        await reader.CloseAsync();

        if (currentStatus == "Completed" && currentDate != request.AppointmentDate)
        {
            throw new ConflictException("Cannot change the date of a completed appointment.");
        }

        await using var checkPatientCmd = new SqlCommand("SELECT IsActive FROM dbo.Patients WHERE IdPatient = @IdPatient", connection);
        checkPatientCmd.Parameters.AddWithValue("@IdPatient", request.IdPatient);
        var patientActiveObj = await checkPatientCmd.ExecuteScalarAsync(ct);
        if (patientActiveObj == null) throw new NotFoundException("Patient not found.");
        if (!(bool)patientActiveObj) throw new ConflictException("Patient is not active.");

        await using var checkDoctorCmd = new SqlCommand("SELECT IsActive FROM dbo.Doctors WHERE IdDoctor = @IdDoctor", connection);
        checkDoctorCmd.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
        var doctorActiveObj = await checkDoctorCmd.ExecuteScalarAsync(ct);
        if (doctorActiveObj == null) throw new NotFoundException("Doctor not found.");
        if (!(bool)doctorActiveObj) throw new ConflictException("Doctor is not active.");

        if (currentDate != request.AppointmentDate)
        {
            await using var checkConflictCmd = new SqlCommand(
                "SELECT COUNT(1) FROM dbo.Appointments WHERE IdDoctor = @IdDoctor AND AppointmentDate = @AppointmentDate AND IdAppointment != @Id", connection);
            checkConflictCmd.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
            checkConflictCmd.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);
            checkConflictCmd.Parameters.AddWithValue("@Id", id);
            var conflictCount = (int)(await checkConflictCmd.ExecuteScalarAsync(ct) ?? 0);
            if (conflictCount > 0)
            {
                throw new ConflictException("Doctor already has an appointment at this time.");
            }
        }

        await using var updateCmd = new SqlCommand("""
            UPDATE dbo.Appointments
            SET IdPatient = @IdPatient,
                IdDoctor = @IdDoctor,
                AppointmentDate = @AppointmentDate,
                Status = @Status,
                Reason = @Reason,
                InternalNotes = @InternalNotes
            WHERE IdAppointment = @Id;
            """, connection);
        updateCmd.Parameters.AddWithValue("@IdPatient", request.IdPatient);
        updateCmd.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
        updateCmd.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);
        updateCmd.Parameters.AddWithValue("@Status", request.Status);
        updateCmd.Parameters.AddWithValue("@Reason", request.Reason);
        updateCmd.Parameters.AddWithValue("@InternalNotes", string.IsNullOrEmpty(request.InternalNotes) ? DBNull.Value : request.InternalNotes);
        updateCmd.Parameters.AddWithValue("@Id", id);

        await updateCmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAppointmentAsync(int id, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var getAppCmd = new SqlCommand("SELECT Status FROM dbo.Appointments WHERE IdAppointment = @Id", connection);
        getAppCmd.Parameters.AddWithValue("@Id", id);
        var statusObj = await getAppCmd.ExecuteScalarAsync(ct);
        if (statusObj == null)
        {
            throw new NotFoundException($"Appointment with ID {id} not found.");
        }

        var status = (string)statusObj;
        if (status == "Completed")
        {
            throw new ConflictException("Cannot delete a completed appointment.");
        }

        await using var deleteCmd = new SqlCommand("DELETE FROM dbo.Appointments WHERE IdAppointment = @Id", connection);
        deleteCmd.Parameters.AddWithValue("@Id", id);
        await deleteCmd.ExecuteNonQueryAsync(ct);
    }
}
