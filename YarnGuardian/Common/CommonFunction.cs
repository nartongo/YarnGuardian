using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YarnGuardian.Server
{
    public sealed class CommonFunction
    {
        public static MainWindow gfrmClient;
        public static long lSendSuccess = 0, lSendConfirm = 0, lTotalSendBytes = 0, lReceiveSuccess = 0, lReceiveConfirm = 0, lTotalReceiveBytes = 0;
        public static DateTime dtStart;

        public static bool Log(string sMsg)
        {
            gfrmClient.Log("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "] " + sMsg);
            return true;
        }
    }
}