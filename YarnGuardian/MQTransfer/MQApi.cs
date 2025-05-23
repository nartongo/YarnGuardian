using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using YarnGuardian.MQHandler;

namespace YarnGuardian.Server
{
    public sealed class MQApi
    {
        public struct TransferResultStruct
        {
            public int Code;

            public string Msg;
        }

        private static readonly MQInstance _instance = new MQInstance();

        private static TransferResultStruct _transferResult = new TransferResultStruct();

        public static MQInstance Instance => _instance;

        public static TransferResultStruct TransferResult => _transferResult;

        public static int SetTransferResult(int iCode, string sMsg)
        {
            _transferResult.Code = iCode;
            _transferResult.Msg = sMsg;
            return _transferResult.Code;
        }

    }
}