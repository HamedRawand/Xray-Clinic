namespace NasimXray.Shared.Dtos.Billing;

// One page of invoices plus the sums over EVERY invoice that matches the filters (not just this page),
// for the totals footer of the Invoices screen.
public class InvoiceListResult : PagedResult<InvoiceDto>
{
    public decimal SumTotal { get; set; }
    public decimal SumPaid { get; set; }
    public decimal SumBalance { get; set; }
}
