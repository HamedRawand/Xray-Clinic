namespace NasimXray.Shared.Enums;

public enum Permission
{
    ViewPatients,
    ManagePatients,

    ViewExamOrders,
    ManageExamOrders,

    ViewReports,
    CreateReport,
    FinalizeReport,
    AmendReport,

    ViewBilling,
    ManageBilling,
    ProcessPayment,

    ManageUsers,
    ManageRoles,

    ManageExamTypes,
    ManageReferringPhysicians,

    ManageBackup,
    ViewAuditLog,
}
