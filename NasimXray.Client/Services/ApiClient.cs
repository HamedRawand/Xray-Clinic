using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using NasimXray.Shared.Dtos.Audit;
using NasimXray.Shared.Dtos.Auth;
using NasimXray.Shared.Dtos.Backup;
using NasimXray.Shared.Dtos.Billing;
using NasimXray.Shared.Dtos.ExamOrders;
using NasimXray.Shared.Dtos.ExamTypes;
using NasimXray.Shared.Dtos.Patients;
using NasimXray.Shared.Dtos.ReferringPhysicians;
using NasimXray.Shared.Dtos.Reports;
using NasimXray.Shared.Dtos.Roles;
using NasimXray.Shared.Dtos.Users;
using NasimXray.Shared.Enums;

namespace NasimXray.Client.Services;

public enum ApiErrorReason
{
    ServerUnreachable,
    Timeout,
    InvalidCredentials,
    AccountLocked,
    Conflict,
    NotFound,
    BadRequest,
    Forbidden,
    Other,
}

public class ApiException(ApiErrorReason reason, string? detail = null) : Exception(detail ?? reason.ToString())
{
    public ApiErrorReason Reason { get; } = reason;
    public string? Detail { get; } = detail;
}

// Thin HttpClient wrapper — the WPF client never talks to the database directly,
// every read/write goes through the API (see ARCHITECTURE_PLAN.md §2 and §10).
public class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient(string baseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(15) };
    }

    public void SetAccessToken(string? accessToken)
    {
        _http.DefaultRequestHeaders.Authorization =
            accessToken is null ? null : new AuthenticationHeaderValue("Bearer", accessToken);
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            var response = await _http.GetAsync("api/system/version");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // Login has its own error mapping — 401/403 mean something different here (bad credentials /
    // locked account) than they do for every other endpoint (unauthenticated / no permission).
    public async Task<LoginResponse> LoginAsync(string username, string password)
    {
        var response = await SendCoreAsync(HttpMethod.Post, "api/auth/login",
            new LoginRequest { Username = username, Password = password });

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new ApiException(ApiErrorReason.InvalidCredentials);
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            throw new ApiException(ApiErrorReason.AccountLocked);

        await ThrowIfError(response);
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return result ?? throw new ApiException(ApiErrorReason.Other, "Empty response.");
    }

    // ── Patients ──────────────────────────────────────────────────────────

    public Task<List<PatientDto>> GetPatientsAsync(string? search = null) =>
        SendAsync<List<PatientDto>>(HttpMethod.Get, $"api/patients{(string.IsNullOrWhiteSpace(search) ? "" : $"?search={Uri.EscapeDataString(search)}")}");

    public Task<PatientDto> GetPatientAsync(int id) =>
        SendAsync<PatientDto>(HttpMethod.Get, $"api/patients/{id}");

    public Task<PatientDto> CreatePatientAsync(PatientUpsertRequest request) =>
        SendAsync<PatientDto>(HttpMethod.Post, "api/patients", request);

    public Task<PatientDto> UpdatePatientAsync(int id, PatientUpsertRequest request) =>
        SendAsync<PatientDto>(HttpMethod.Put, $"api/patients/{id}", request);

    // ── Referring physicians ─────────────────────────────────────────────

    public Task<List<ReferringPhysicianDto>> GetReferringPhysiciansAsync(bool includeInactive = false) =>
        SendAsync<List<ReferringPhysicianDto>>(HttpMethod.Get, $"api/referring-physicians?includeInactive={includeInactive}");

    public Task<ReferringPhysicianDto> CreateReferringPhysicianAsync(ReferringPhysicianUpsertRequest request) =>
        SendAsync<ReferringPhysicianDto>(HttpMethod.Post, "api/referring-physicians", request);

    public Task<ReferringPhysicianDto> UpdateReferringPhysicianAsync(int id, ReferringPhysicianUpsertRequest request) =>
        SendAsync<ReferringPhysicianDto>(HttpMethod.Put, $"api/referring-physicians/{id}", request);

    // ── Exam types ────────────────────────────────────────────────────────

    public Task<List<ExamTypeDto>> GetExamTypesAsync(bool includeInactive = false) =>
        SendAsync<List<ExamTypeDto>>(HttpMethod.Get, $"api/exam-types?includeInactive={includeInactive}");

    public Task<ExamTypeDto> CreateExamTypeAsync(ExamTypeUpsertRequest request) =>
        SendAsync<ExamTypeDto>(HttpMethod.Post, "api/exam-types", request);

    public Task<ExamTypeDto> UpdateExamTypeAsync(int id, ExamTypeUpsertRequest request) =>
        SendAsync<ExamTypeDto>(HttpMethod.Put, $"api/exam-types/{id}", request);

    public Task<MeasurementFieldDto> AddMeasurementFieldAsync(int examTypeId, MeasurementFieldUpsertRequest request) =>
        SendAsync<MeasurementFieldDto>(HttpMethod.Post, $"api/exam-types/{examTypeId}/measurement-fields", request);

    public Task<MeasurementFieldDto> UpdateMeasurementFieldAsync(int examTypeId, int fieldId, MeasurementFieldUpsertRequest request) =>
        SendAsync<MeasurementFieldDto>(HttpMethod.Put, $"api/exam-types/{examTypeId}/measurement-fields/{fieldId}", request);

    public Task DeleteMeasurementFieldAsync(int examTypeId, int fieldId) =>
        SendNoContentAsync(HttpMethod.Delete, $"api/exam-types/{examTypeId}/measurement-fields/{fieldId}");

    // ── Exam orders ───────────────────────────────────────────────────────

    public Task<List<ExamOrderDto>> GetExamOrdersAsync(ExamOrderStatus? status = null, int? patientId = null, bool? uninvoiced = null)
    {
        var query = new List<string>();
        if (status.HasValue) query.Add($"status={status.Value}");
        if (patientId.HasValue) query.Add($"patientId={patientId.Value}");
        if (uninvoiced.HasValue) query.Add($"uninvoiced={uninvoiced.Value}");
        var qs = query.Count > 0 ? "?" + string.Join("&", query) : "";
        return SendAsync<List<ExamOrderDto>>(HttpMethod.Get, $"api/exam-orders{qs}");
    }

    public Task<ExamOrderDto> GetExamOrderAsync(int id) =>
        SendAsync<ExamOrderDto>(HttpMethod.Get, $"api/exam-orders/{id}");

    public Task<ExamOrderDto> CreateExamOrderAsync(ExamOrderCreateRequest request) =>
        SendAsync<ExamOrderDto>(HttpMethod.Post, "api/exam-orders", request);

    public Task<ExamOrderDto> UpdateExamOrderStatusAsync(int id, ExamOrderStatusUpdateRequest request) =>
        SendAsync<ExamOrderDto>(HttpMethod.Put, $"api/exam-orders/{id}/status", request);

    // ── Reports ───────────────────────────────────────────────────────────

    public async Task<ReportDto?> GetReportAsync(int examOrderId)
    {
        var response = await _http.GetAsync($"api/exam-orders/{examOrderId}/report");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await ThrowIfError(response);
        return await response.Content.ReadFromJsonAsync<ReportDto>();
    }

    public Task<ReportDto> SaveReportDraftAsync(int examOrderId, ReportSaveRequest request) =>
        SendAsync<ReportDto>(HttpMethod.Put, $"api/exam-orders/{examOrderId}/report", request);

    public Task<ReportDto> FinalizeReportAsync(int examOrderId, ReportFinalizeRequest request) =>
        SendAsync<ReportDto>(HttpMethod.Post, $"api/exam-orders/{examOrderId}/report/finalize", request);

    public Task<ReportDto> AmendReportAsync(int examOrderId, ReportAmendRequest request) =>
        SendAsync<ReportDto>(HttpMethod.Post, $"api/exam-orders/{examOrderId}/report/amend", request);

    // ── Billing ───────────────────────────────────────────────────────────

    public Task<List<InvoiceDto>> GetInvoicesAsync(int? patientId = null) =>
        SendAsync<List<InvoiceDto>>(HttpMethod.Get, $"api/invoices{(patientId.HasValue ? $"?patientId={patientId.Value}" : "")}");

    public Task<InvoiceDto> GetInvoiceAsync(int id) =>
        SendAsync<InvoiceDto>(HttpMethod.Get, $"api/invoices/{id}");

    public Task<InvoiceDto> CreateInvoiceAsync(InvoiceCreateRequest request) =>
        SendAsync<InvoiceDto>(HttpMethod.Post, "api/invoices", request);

    public Task<InvoiceDto> RecordPaymentAsync(int invoiceId, PaymentCreateRequest request) =>
        SendAsync<InvoiceDto>(HttpMethod.Post, $"api/invoices/{invoiceId}/payments", request);

    // ── Users, roles, audit ──────────────────────────────────────────────

    public Task ChangePasswordAsync(string currentPassword, string newPassword) =>
        SendNoContentAsync(HttpMethod.Post, "api/auth/change-password",
            new ChangePasswordRequest { CurrentPassword = currentPassword, NewPassword = newPassword });

    public Task<List<UserDto>> GetUsersAsync() =>
        SendAsync<List<UserDto>>(HttpMethod.Get, "api/users");

    public Task<UserDto> CreateUserAsync(UserCreateRequest request) =>
        SendAsync<UserDto>(HttpMethod.Post, "api/users", request);

    public Task<UserDto> UpdateUserAsync(int id, UserUpdateRequest request) =>
        SendAsync<UserDto>(HttpMethod.Put, $"api/users/{id}", request);

    public Task ResetUserPasswordAsync(int id, string newPassword) =>
        SendNoContentAsync(HttpMethod.Post, $"api/users/{id}/reset-password", new ResetPasswordRequest { NewPassword = newPassword });

    public Task<UserDto> UnlockUserAsync(int id) =>
        SendAsync<UserDto>(HttpMethod.Post, $"api/users/{id}/unlock");

    public Task<List<CustomRoleDto>> GetCustomRolesAsync() =>
        SendAsync<List<CustomRoleDto>>(HttpMethod.Get, "api/custom-roles");

    public Task<CustomRoleDto> CreateCustomRoleAsync(CustomRoleUpsertRequest request) =>
        SendAsync<CustomRoleDto>(HttpMethod.Post, "api/custom-roles", request);

    public Task<CustomRoleDto> UpdateCustomRoleAsync(int id, CustomRoleUpsertRequest request) =>
        SendAsync<CustomRoleDto>(HttpMethod.Put, $"api/custom-roles/{id}", request);

    public Task<List<AuditLogDto>> GetAuditLogsAsync() =>
        SendAsync<List<AuditLogDto>>(HttpMethod.Get, "api/audit-logs");

    // ── Backup ────────────────────────────────────────────────────────────

    public Task<List<BackupFileDto>> GetBackupsAsync() =>
        SendAsync<List<BackupFileDto>>(HttpMethod.Get, "api/backup");

    public Task<BackupFileDto> CreateBackupAsync() =>
        SendAsync<BackupFileDto>(HttpMethod.Post, "api/backup/create");

    public Task RestoreBackupAsync(string fileName, string password) =>
        SendNoContentAsync(HttpMethod.Post, "api/backup/restore", new RestoreBackupRequest { FileName = fileName, Password = password });

    // ── Plumbing ──────────────────────────────────────────────────────────

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body = null)
    {
        var response = await SendCoreAsync(method, url, body);
        await ThrowIfError(response);
        var result = await response.Content.ReadFromJsonAsync<T>();
        return result ?? throw new ApiException(ApiErrorReason.Other, "Empty response.");
    }

    private async Task SendNoContentAsync(HttpMethod method, string url, object? body = null)
    {
        var response = await SendCoreAsync(method, url, body);
        await ThrowIfError(response);
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string url, object? body = null)
    {
        try
        {
            var request = new HttpRequestMessage(method, url);
            if (body is not null) request.Content = JsonContent.Create(body);
            return await _http.SendAsync(request);
        }
        catch (HttpRequestException)
        {
            throw new ApiException(ApiErrorReason.ServerUnreachable);
        }
        catch (TaskCanceledException)
        {
            throw new ApiException(ApiErrorReason.Timeout);
        }
    }

    private static async Task ThrowIfError(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;

        var reason = response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => ApiErrorReason.InvalidCredentials,
            System.Net.HttpStatusCode.Forbidden => ApiErrorReason.Forbidden,
            System.Net.HttpStatusCode.Conflict => ApiErrorReason.Conflict,
            System.Net.HttpStatusCode.NotFound => ApiErrorReason.NotFound,
            System.Net.HttpStatusCode.BadRequest => ApiErrorReason.BadRequest,
            _ => ApiErrorReason.Other,
        };

        string? detail = null;
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ErrorMessage>();
            detail = problem?.Message;
        }
        catch { /* body wasn't JSON — ignore, fall back to the generic reason text */ }

        throw new ApiException(reason, detail);
    }

    private class ErrorMessage
    {
        public string? Message { get; set; }
    }
}
