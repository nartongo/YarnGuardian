namespace YarnGuardian.Common
{
    public class RepairTaskMsg
    {
        public int SideNumber { get; set; }
        public string TaskId { get; set; }
        public int[] BreakPoints { get; set; }
        public string OriginalMessage { get; set; }
    }
}