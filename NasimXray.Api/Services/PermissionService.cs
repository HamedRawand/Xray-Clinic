using NasimXray.Api.Models;
using NasimXray.Shared.Enums;

namespace NasimXray.Api.Services;

public static class PermissionService
{
    private static readonly Dictionary<UserRole, HashSet<Permission>> DefaultRolePermissions = new()
    {
        [UserRole.Admin] = Enum.GetValues<Permission>().ToHashSet(),

        [UserRole.Radiologist] = new()
        {
            Permission.ViewPatients,
            Permission.ViewExamOrders,
            Permission.ManageExamOrders,
            Permission.ViewReports,
            Permission.CreateReport,
            Permission.FinalizeReport,
            Permission.AmendReport,
        },

        [UserRole.Technician] = new()
        {
            Permission.ViewPatients,
            Permission.ViewExamOrders,
            Permission.ManageExamOrders,
            Permission.ViewReports,
        },

        [UserRole.Receptionist] = new()
        {
            Permission.ViewPatients,
            Permission.ManagePatients,
            Permission.ViewExamOrders,
            Permission.ManageExamOrders,
            Permission.ViewBilling,
            Permission.ManageBilling,
            Permission.ProcessPayment,
            Permission.ManageReferringPhysicians,
            Permission.ApplyDiscount,
        },
    };

    public static HashSet<Permission> GetPermissions(User user) =>
        user.CustomRole is not null
            ? user.CustomRole.GetPermissions()
            : DefaultRolePermissions.TryGetValue(user.Role, out var perms) ? perms : [];

    public static bool Can(User user, Permission permission) =>
        GetPermissions(user).Contains(permission);
}
