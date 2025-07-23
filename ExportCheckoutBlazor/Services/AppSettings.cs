namespace NDAProcesses.Services
{
    public class AppSettings
    {
        public int DaysBack { get; set; }
        public TimeSpan ScheduledTime { get; set; }
        public string[] Recipients { get; set; }
    }
}
