using System.Text.Json;

using NetMQ;
using NetMQ.Sockets;

namespace YarnGuardian.MQHandler
{
    public class MsgHandler
    {
        ClientSocket clientSocket = new ClientSocket();

        public string HandlerStatusMsg { get; set; } = string.Empty;

        public bool Init(string sIP, string sPortNo)
        {
            try
            {
                this.HandlerStatusMsg = string.Empty;
                string sAddress = "tcp://" + sIP + ":" + sPortNo;
                clientSocket.Connect(sAddress);
                return true;
            }
            catch (Exception e)
            {
                HandlerStatusMsg = e.Message;
                return false;
            }
        }

        public bool Send<T>(IMQMsg<T> msg)
        {
            //try
            //{
                string sMsg = JsonSerializer.Serialize(msg);
                clientSocket.Send(sMsg);
                return true;
            //}
            //catch (Exception e)
            //{
            //    HandlerStatusMsg = e.Message;
            //}
        }

        public async Task<bool> SendAsync<T>(IMQMsg<T> msg)
        {
            //try
            //{
            string sMsg = JsonSerializer.Serialize(msg);
            await clientSocket.SendAsync(sMsg);
            return true;
            //}
            //catch (Exception e)
            //{
            //    HandlerStatusMsg = e.Message;
            //}
        }

        public MQMsg<T> Receive<T>()
        {
            try
            {
                string sMsg = string.Empty;

                sMsg = clientSocket.ReceiveString();
                var msg = JsonSerializer.Deserialize<MQMsg<T>>(sMsg) ?? new MQMsg<T>();
                return msg;
            }
            catch (Exception e)
            {
                return (new MQMsg<T>()).Failed(e.Message);
            }
        }

        public async Task<MQMsg<T>> ReceiveAsync<T>()
        {
            try
            {
                string sMsg = string.Empty;

                sMsg = await clientSocket.ReceiveStringAsync();
                var msg = JsonSerializer.Deserialize<MQMsg<T>>(sMsg) ?? new MQMsg<T>();
                return msg;
            }
            catch (Exception e)
            {
                return (new MQMsg<T>()).Failed(e.Message);
            }
        }
    }
}
