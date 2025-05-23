using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YarnGuardian.Common
{
    public class StatusReportModel
    {
        public string Type { get; set; } = "status_report";

        public string RobotId { get; set; } = string.Empty;

        public DateTime TimeStamp { get; set; } = DateTime.Now;

        public string Status { get; set; } = string.Empty;

        public double Power { get; set; } = 0d;

        public int CurrentLaneId { get; set; } = 0;

        public string CurrentPosition { get; set; } = string.Empty;

        public string Direction { get; set; } = string.Empty;

        public double Speed { get; set; } = 0d;
    }
}