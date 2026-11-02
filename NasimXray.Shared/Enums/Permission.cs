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

    // Lower an exam's charge (discount / free). Appended last so stored permission lists stay valid.
    ApplyDiscount,

    // Edit the clinic's name, contact details and logo. Appended last (see ApplyDiscount).
    ManageClinicSettings,
}
