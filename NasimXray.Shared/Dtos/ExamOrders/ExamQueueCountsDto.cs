namespace NasimXray.Shared.Dtos.ExamOrders;

// Sizes of the work queues shown as one-click filters on the Exams screen (all time, not just today).
public class ExamQueueCountsDto
{
    public int Pending { get; set; }
    public int InProgress { get; set; }
    public int Completed { get; set; }
    public int NotInvoiced { get; set; }
    public int Unpaid { get; set; }
}
