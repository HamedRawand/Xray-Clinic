using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using NasimXray.Shared.Dtos.Audit;
using NasimXray.Shared.Dtos.Auth;
using NasimXray.Shared.Dtos.Backup;
using NasimXray.Shared.Dtos.Billing;
using NasimXray.Shared.Dtos;
using NasimXray.Shared.Dtos.CheckIn;
using NasimXray.Shared.Dtos.Clinic;
using NasimXray.Shared.Dtos.ExamOrders;
using NasimXray.Shared.Dtos.Stats;
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
    private string? _refreshToken;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    // Raised when the access token has expired AND the refresh token is also invalid/expired —
    // the caller (App/MainWindow) should send the user back to LoginWindow. Network resilience
    // §9 hardening: previously an expired access token just meant every subsequent action failed
    // with "invalid credentials" and no recovery until the user noticed and manually signed out.
    public event Action? SessionExpired;

    // expectedCertThumbprint: for an https:// baseUrl, the server's certificate must match this
    // exact thumbprint (trust-on-first-use pin — see ClientSettings.ServerCertificateThumbprint).
    // Left null for an http:// baseUrl (no certificate involved) or when HTTPS hasn't been
    // trusted yet, in which case any HTTPS connection attempt fails closed (safe default).
    public ApiClient(string baseUrl, string? expectedCertThumbprint = null)
    {
        var handler = new HttpClientHandler();
        if (baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            handler.ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
                cert is not null &&
                !string.IsNullOrEmpty(expectedCertThumbprint) &&
                string.Equals(cert.Thumbprint, expectedCertThumbprint, StringComparison.OrdinalIgnoreCase);
        }

        _http = new HttpClient(handler) { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(15) };
    }

    // Connects without validating the certificate at all, purely to read what thumbprint the
    // server presents — used by ServerSettingsWindow's trust-on-first-use flow so the admin can
    // see and confirm the cert BEFORE it's pinned for real use. Never used for any authenticated
    // or data-carrying request.
    public static async Task<string?> ProbeServerCertificateThumbprintAsync(string httpsBaseUrl)
    {
        string? thumbprint = null;
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
            {
                thumbprint = cert?.Thumbprint;
                return true; // accept anything here — this call never sends credentials or data
            },
        };

        try
        {
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            await http.GetAsync(new Uri(new Uri(httpsBaseUrl), "api/system/version"));
        }
        catch
        {
            return null;
        }

        return thumbprint;
    }

    public void SetAccessToken(string? accessToken)
    {
        _http.DefaultRequestHeaders.Authorization =
            accessToken is null ? null : new AuthenticationHeaderValue("Bearer", accessToken);
    }

    public void SetSession(string accessToken, string refreshToken)
    {
        SetAccessToken(accessToken);
        _refreshToken = refreshToken;
    }

    public void ClearSession()
    {
        SetAccessToken(null);
        _refreshToken = null;
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

    public Task<PagedResult<PatientDto>> GetPatientsAsync(string? search = null, int page = 1, int pageSize = 50) =>
        SendAsync<PagedResult<PatientDto>>(HttpMethod.Get,
            $"api/patients?page={page}&pageSize={pageSize}{(string.IsNullOrWhiteSpace(search) ? "" : $"&search={Uri.EscapeDataString(search.Trim())}")}");

    public Task<List<PatientDto>> GetPossibleDuplicatePatientsAsync(string? fullName, string? phoneNumber) =>
        SendAsync<List<PatientDto>>(HttpMethod.Get,
            $"api/patients/possible-duplicates?fullName={Uri.EscapeDataString(fullName ?? "")}&phoneNumber={Uri.EscapeDataString(phoneNumber ?? "")}");

    public Task<CheckInResponse> CheckInAsync(CheckInRequest request) =>
        SendAsync<CheckInResponse>(HttpMethod.Post, "api/check-in", request);

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

    private static List<string> ExamFilterParts(ExamOrderSearchQuery q)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(q.Search)) parts.Add($"search={Uri.EscapeDataString(q.Search.Trim())}");
        if (q.DateFrom.HasValue) parts.Add($"dateFrom={q.DateFrom.Value:yyyy-MM-dd}");
        if (q.DateTo.HasValue) parts.Add($"dateTo={q.DateTo.Value:yyyy-MM-dd}");
        if (q.ExamTypeId.HasValue) parts.Add($"examTypeId={q.ExamTypeId.Value}");
        if (!string.IsNullOrWhiteSpace(q.Modality)) parts.Add($"modality={Uri.EscapeDataString(q.Modality)}");
        if (q.Status.HasValue) parts.Add($"status={q.Status.Value}");
        if (q.ReferringPhysicianId.HasValue) parts.Add($"referringPhysicianId={q.ReferringPhysicianId.Value}");
        if (q.Billing != ExamBillingFilter.Any) parts.Add($"billing={q.Billing}");
        return parts;
    }

    public Task<ExamOrderSearchResponse> SearchExamOrdersAsync(ExamOrderSearchQuery q)
    {
        var parts = ExamFilterParts(q);
        parts.Add($"page={q.Page}");
        parts.Add($"pageSize={q.PageSize}");
        return SendAsync<ExamOrderSearchResponse>(HttpMethod.Get, $"api/exam-orders/search?{string.Join("&", parts)}");
    }

    // Every exam matching the filters (no paging) — for the Excel export.
    public Task<List<ExamOrderDto>> ExportExamOrdersAsync(ExamOrderSearchQuery q)
    {
        var qs = string.Join("&", ExamFilterParts(q));
        return SendAsync<List<ExamOrderDto>>(HttpMethod.Get, $"api/exam-orders/export{(qs.Length > 0 ? "?" + qs : "")}");
    }

    public Task<DashboardDto> GetDashboardAsync() =>
        SendAsync<DashboardDto>(HttpMethod.Get, "api/stats/dashboard");

    public Task<StatsReportDto> GetStatsReportAsync(DateOnly from, DateOnly to) =>
        SendAsync<StatsReportDto>(HttpMethod.Get, $"api/stats/report?dateFrom={from:yyyy-MM-dd}&dateTo={to:yyyy-MM-dd}");

    public Task<ExamOrderDto> UpdateExamDiscountAsync(int id, ExamDiscountRequest request) =>
        SendAsync<ExamOrderDto>(HttpMethod.Put, $"api/exam-orders/{id}/discount", request);

    public Task<ExamQueueCountsDto> GetExamQueueCountsAsync() =>
        SendAsync<ExamQueueCountsDto>(HttpMethod.Get, "api/exam-orders/queue-counts");

    public Task<ExamOrderDto> GetExamOrderAsync(int id) =>
        SendAsync<ExamOrderDto>(HttpMethod.Get, $"api/exam-orders/{id}");

    public Task<ExamOrderDto> CreateExamOrderAsync(ExamOrderCreateRequest request) =>
        SendAsync<ExamOrderDto>(HttpMethod.Post, "api/exam-orders", request);

    public Task<ExamOrderDto> UpdateExamOrderStatusAsync(int id, ExamOrderStatusUpdateRequest request) =>
        SendAsync<ExamOrderDto>(HttpMethod.Put, $"api/exam-orders/{id}/status", request);

    // ── Reports ───────────────────────────────────────────────────────────

    public async Task<ReportDto?> GetReportAsync(int examOrderId)
    {
        var response = await SendCoreAsync(HttpMethod.Get, $"api/exam-orders/{examOrderId}/report");
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

    public Task<InvoiceListResult> GetInvoicesAsync(int? patientId = null, string? search = null,
        InvoiceStatus? status = null, int page = 1, int pageSize = 50)
    {
        var parts = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (patientId.HasValue) parts.Add($"patientId={patientId.Value}");
        if (!string.IsNullOrWhiteSpace(search)) parts.Add($"search={Uri.EscapeDataString(search.Trim())}");
        if (status.HasValue) parts.Add($"status={status.Value}");
        return SendAsync<InvoiceListResult>(HttpMethod.Get, $"api/invoices?{string.Join("&", parts)}");
    }

    // Every invoice matching the filters (no paging) — for the Excel export.
    public Task<List<InvoiceDto>> ExportInvoicesAsync(string? search = null, InvoiceStatus? status = null)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(search)) parts.Add($"search={Uri.EscapeDataString(search.Trim())}");
        if (status.HasValue) parts.Add($"status={status.Value}");
        return SendAsync<List<InvoiceDto>>(HttpMethod.Get, $"api/invoices/export{(parts.Count > 0 ? "?" + string.Join("&", parts) : "")}");
    }

    public Task<InvoiceDto> GetInvoiceAsync(int id) =>
        SendAsync<InvoiceDto>(HttpMethod.Get, $"api/invoices/{id}");

    public Task<InvoiceDto> CreateInvoiceAsync(InvoiceCreateRequest request) =>
        SendAsync<InvoiceDto>(HttpMethod.Post, "api/invoices", request);

    public Task<InvoiceDto> RecordPaymentAsync(int invoiceId, PaymentCreateRequest request) =>
        SendAsync<InvoiceDto>(HttpMethod.Post, $"api/invoices/{invoiceId}/payments", request);

    // ── Users, roles, audit ──────────────────────────────────────────────

    public async Task<bool> VerifyPasswordAsync(string password)
    {
        var response = await SendCoreAsync(HttpMethod.Post, "api/auth/verify-password", new VerifyPasswordRequest { Password = password });
        return response.IsSuccessStatusCode;
    }

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

    public Task<PagedResult<AuditLogDto>> GetAuditLogsAsync(string? search = null, string? action = null,
        DateOnly? dateFrom = null, DateOnly? dateTo = null, int page = 1, int pageSize = 50)
    {
        var parts = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(search)) parts.Add($"search={Uri.EscapeDataString(search.Trim())}");
        if (!string.IsNullOrWhiteSpace(action)) parts.Add($"action={Uri.EscapeDataString(action)}");
        if (dateFrom.HasValue) parts.Add($"dateFrom={dateFrom.Value:yyyy-MM-dd}");
        if (dateTo.HasValue) parts.Add($"dateTo={dateTo.Value:yyyy-MM-dd}");
        return SendAsync<PagedResult<AuditLogDto>>(HttpMethod.Get, $"api/audit-logs?{string.Join("&", parts)}");
    }

    public Task<List<string>> GetAuditActionsAsync() =>
        SendAsync<List<string>>(HttpMethod.Get, "api/audit-logs/actions");

    // ── Backup ────────────────────────────────────────────────────────────

    public Task<List<BackupFileDto>> GetBackupsAsync() =>
        SendAsync<List<BackupFileDto>>(HttpMethod.Get, "api/backup");

    public Task<BackupFileDto> CreateBackupAsync() =>
        SendAsync<BackupFileDto>(HttpMethod.Post, "api/backup/create");

    public Task RestoreBackupAsync(string fileName, string password) =>
        SendNoContentAsync(HttpMethod.Post, "api/backup/restore", new RestoreBackupRequest { FileName = fileName, Password = password });

    // ── Clinic profile ────────────────────────────────────────────────────

    // Public (works before sign-in): the login screen shows the clinic name.
    public Task<ClinicProfileDto> GetClinicProfileAsync() =>
        SendAsync<ClinicProfileDto>(HttpMethod.Get, "api/system/clinic");

    public async Task<byte[]?> GetClinicLogoAsync()
    {
        var response = await SendCoreAsync(HttpMethod.Get, "api/system/clinic/logo");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await ThrowIfError(response);
        return await response.Content.ReadAsByteArrayAsync();
    }

    public Task<ClinicProfileDto> UpdateClinicProfileAsync(ClinicProfileUpdateRequest request) =>
        SendAsync<ClinicProfileDto>(HttpMethod.Put, "api/clinic", request);

    public Task<ClinicProfileDto> UploadClinicLogoAsync(byte[] imageBytes) =>
        SendAsync<ClinicProfileDto>(HttpMethod.Put, "api/clinic/logo", new RawBody(imageBytes, "application/octet-stream"));

    public Task<ClinicProfileDto> RemoveClinicLogoAsync() =>
        SendAsync<ClinicProfileDto>(HttpMethod.Delete, "api/clinic/logo");

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

    private static readonly HashSet<string> AuthEndpoints = ["api/auth/login", "api/auth/refresh", "api/auth/verify-password"];

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string url, object? body = null)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(BuildRequest(method, url, body));
        }
        catch (HttpRequestException)
        {
            throw new ApiException(ApiErrorReason.ServerUnreachable);
        }
        catch (TaskCanceledException)
        {
            throw new ApiException(ApiErrorReason.Timeout);
        }

        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized || AuthEndpoints.Contains(url) || _refreshToken is null)
            return response;

        // Access token expired mid-session — try a silent refresh once, then retry the original
        // call. A semaphore ensures concurrent requests that all hit a 401 at once share a single
        // refresh instead of each racing to use the same (single-use) refresh token.
        var refreshed = await TryRefreshTokenAsync();
        if (!refreshed)
        {
            ClearSession();
            SessionExpired?.Invoke();
            return response;
        }

        try
        {
            return await _http.SendAsync(BuildRequest(method, url, body));
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

    private async Task<bool> TryRefreshTokenAsync()
    {
        await _refreshLock.WaitAsync();
        try
        {
            if (_refreshToken is null) return false;

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(BuildRequest(HttpMethod.Post, "api/auth/refresh", new RefreshRequest { RefreshToken = _refreshToken }));
            }
            catch
            {
                return false;
            }

            if (!response.IsSuccessStatusCode) return false;

            var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
            if (login is null) return false;

            SetSession(login.AccessToken, login.RefreshToken);
            SessionService.UpdateTokens(login);
            return true;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, string url, object? body)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is RawBody raw)
            request.Content = new ByteArrayContent(raw.Bytes) { Headers = { ContentType = new MediaTypeHeaderValue(raw.ContentType) } };
        else if (body is not null)
            request.Content = JsonContent.Create(body);
        return request;
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

// A request body that is raw bytes (an image), not JSON.
internal sealed record RawBody(byte[] Bytes, string ContentType);
