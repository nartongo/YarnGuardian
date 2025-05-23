using System.Text.Json;
using YarnGuardian.MQHandler;
using YarnGuardian.Coordinator;

namespace YarnGuardian.Server
{
    public class MQInstance
    {
		private MsgHandler session;
        private Thread thdReceive;
        private bool bRunSend = false;
        private bool bRunReceive = false;
        private bool bAvaiableReceive = true;
        private string sServerAddress;

        public MQInstance()
        {
            session = new MsgHandler();
            thdReceive = new Thread(new ThreadStart(Dispatch));
        }

        public bool Connect(string sIP, string sPortNo)
        {
            try
            {
                sServerAddress = "tcp://" + sIP + ":" + sPortNo;
                session.Init(sIP, sPortNo);
                thdReceive.Start();
            }
            catch (Exception ex)
            {
                MQApi.SetTransferResult(1, ex.Message);
                return false;
            }
            return true;
        }

        public void Dispatch()
        {
            bAvaiableReceive = true;
            while (true)
            {
                MQMsg<Object> msgReceive = null;
                if (bAvaiableReceive)
                {
                    msgReceive = session.Receive<Object>();
                }

                bAvaiableReceive = false;
                OnReceive(msgReceive);
                bAvaiableReceive = true;
            }
        }

        public async void OnReceive<T>(MQMsg<T> msgReceive)
        {
            try
            {
                MainCoordinator _coordinator = new MainCoordinator("for test");
                string sMsg = JsonSerializer.Serialize(msgReceive);
                CommonFunction.Log(" 接收自 " + sServerAddress + " ：" + sMsg);
                CommonFunction.lTotalReceiveBytes += sMsg.Length * 2;
                CommonFunction.lReceiveSuccess += 1;
                switch (msgReceive.Module)
                {
                    case "schedule":
                    case "agv":
                        await _coordinator.MainCoordinatorDispatch(session, msgReceive);
                        break;
                    default:
                        TunerDispatch(msgReceive.Module, session, msgReceive);
                        break;
                }
                //if (MQApi.TransferResult.Code != 0)
                //{
                //    IMQMsg<T> reply = MQMsg.Failed<T>(msgReceive);
                //    session.Send<T>(reply);
                //}
            }
            catch (Exception)
            {
            }
        }

        public void BackgroundSend<T>(MQMsg<T> msgSend)
        {
            Task.Run(() =>
            {
                string sMsg = JsonSerializer.Serialize(msgSend);
                session.Send(msgSend);
                CommonFunction.Log("发送给 " + sServerAddress + " ：" + sMsg);
                CommonFunction.lTotalSendBytes += sMsg.Length * 2;
                CommonFunction.lSendSuccess += 1;
            });
        }

        public void Term()
        {
            session = null;
        }

        protected virtual bool TunerDispatch<T>(string module, MsgHandler session, MQMsg<T> msg)
        {
            return false;
        }

    }
}
