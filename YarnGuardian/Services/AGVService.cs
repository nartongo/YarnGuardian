using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
// using YarnGuardian.Common; // Assuming EventHub is in this namespace or defined elsewhere

namespace YarnGuardian.Services
{
    /// <summary>
    /// AGV 状态数据类 (已更新)
    /// </summary>
    public class AgvStatus
    {
        // 为状态查询 (需求 3) 准备的属性
        public float? X { get; set; }             // 位置 X
        public float? Y { get; set; }             // 位置 Y
        public float? Vx { get; set; }            // 速度 (行走速度)
        public float? StateOfCharge { get; set; } // 电池百分比

        // 为判断是否到达目标点 (需求 2) 准备的属性
        public byte? AgvOperationalStatus { get; set; } // AGV 操作状态 (例如：空闲, 运行中)
        public uint? CurrentPointId { get; set; }       // AGV报告的最后通过或当前所在的点ID
    }

    /// <summary>
    /// AGV 通信服务类 (已修改)
    /// </summary>
    public class AGVService
    {
        // private EventHub _eventHub; // Uncomment if EventHub is used
        private UdpClient _udpClient;
        private IPEndPoint _agvEndPoint;
        private const string AGV_IP_DEFAULT = "192.168.100.178"; 
        private const int AGV_PORT_DEFAULT = 17804; 
        private ushort _sequenceNumber = 0;
        private readonly byte[] _authCode = new byte[16]; // TODO: 替换为实际授权码

        // 用于简单导航 (0x16 命令) 的跟踪字段
        private uint _lastCommandedTargetPointId = 0;
        private bool _isNavigateToPointCommandActive = false; // 标志，指示是否正在监控一个简单点导航指令的到达情况

        /// <summary>
        /// 构造函数
        /// </summary>
        public AGVService()
        {
            // _eventHub = EventHub.Instance; // Uncomment if EventHub is used
            // TODO: 使用供应商提供的实际授权码初始化 _authCode
            // 例如: _authCode = new byte[] { 0x01, 0x02, ..., 0x10 };
            // For demonstration, initializing with zeros. Replace with actual auth code.
            for (int i = 0; i < _authCode.Length; i++) _authCode[i] = 0x00;
            Console.WriteLine("[AGVService] 请确保在构造函数中初始化实际的授权码 _authCode。");
        }

        /// <summary>
        /// 配置 AGV 连接参数
        /// </summary>
        /// <param name="ipAddress">AGV 的 IP 地址</param>
        /// <param name="port">AGV 的端口号</param>
        public void Configure(string ipAddress = AGV_IP_DEFAULT, int port = AGV_PORT_DEFAULT)
        {
            try
            {
                _udpClient = new UdpClient();
                _agvEndPoint = new IPEndPoint(IPAddress.Parse(ipAddress), port);
                Console.WriteLine($"[AGVService] 已配置 AGV 连接: IP={ipAddress}, Port={port}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AGVService] 配置 AGV 连接失败: {ex.Message}");
                _udpClient?.Close(); // Ensure UdpClient is closed on configuration failure
                _udpClient = null;
                throw;
            }
        }

        /// <summary>
        /// 启动 AGV 通信服务
        /// </summary>
        public void Start()
        {
            try
            {
                if (_udpClient == null)
                {
                    Configure(); // 如果尚未配置，则使用默认值进行配置
                }
                // ListenForResponsesAsync(); // If continuous listening is needed
                Console.WriteLine("[AGVService] AGV 通信服务已启动。");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AGVService] 启动 AGV 通信服务失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 停止 AGV 通信服务
        /// </summary>
        public void Stop()
        {
            try
            {
                _udpClient?.Close();
                _udpClient = null;
                Console.WriteLine("[AGVService] AGV 通信服务已停止。");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AGVService] 停止 AGV 通信服务失败: {ex.Message}");
            }
        }

        #region 报文构建 (Packet Building)
        /// <summary>
        /// 获取下一个通信序列号
        /// </summary>
        private ushort GetNextSequenceNumber()
        {
            return _sequenceNumber++;
        }

        /// <summary>
        /// 构建报文头
        /// </summary>
        /// <param name="commandCode">命令码</param>
        /// <param name="dataLength">数据区长度</param>
        /// <returns>构建好的报文头字节数组</returns>
        private byte[] BuildPacketHeader(byte commandCode, ushort dataLength)
        {
            byte[] header = new byte[0x1C]; // 报文头固定长度 28 字节
            Buffer.BlockCopy(_authCode, 0, header, 0x00, 16); // 授权码
            header[0x10] = 0x01;       // 协议版本号 (固定为 0x01)
            header[0x11] = 0x00;       // 报文类型 (0x00:请求报文)
            ushort seqNum = GetNextSequenceNumber();
            header[0x12] = (byte)(seqNum & 0xFF);       // 通信序列号 (低字节)
            header[0x13] = (byte)(seqNum >> 8);         // 通信序列号 (高字节)
            header[0x14] = 0x10;       // 服务码 (固定为 0x10)
            header[0x15] = commandCode; // 命令码
            header[0x16] = 0x00;       // 执行码 (请求报文时为0)
            header[0x17] = 0x00;       // 预留, 置0
            header[0x18] = (byte)(dataLength & 0xFF);   // 报文数据区长度 (低字节)
            header[0x19] = (byte)(dataLength >> 8);     // 报文数据区长度 (高字节)
            header[0x1A] = 0x00;       // 预留, 置0
            header[0x1B] = 0x00;       // 预留, 置0
            return header;
        }

        /// <summary>
        /// 构建简单导航命令 (0x16)
        /// </summary>
        /// <param name="targetPointId">目标路径点ID</param>
        private byte[] BuildSimpleNavigationCommand(uint targetPointId)
        {
            const byte COMMAND_CODE = 0x16; // 导航控制命令码
            // 根据文档3.4.10, 0x16命令的数据区结构
            // 简单导航到路径点时，数据区长度会根据参数变化，这里设定为基础长度
            // 操作(U8), 导航方式(U8), 是否指定导航路径(U8), 是否启用交通管理(U8), 路径点ID(U8[8])
            // 00 00 00 00 XX XX XX XX XX XX XX XX (路径点ID)
            // 总长度 4 + 8 = 12 bytes for this specific simple case
            const ushort DATA_LENGTH = 12; 

            byte[] header = BuildPacketHeader(COMMAND_CODE, DATA_LENGTH);
            byte[] packet = new byte[header.Length + DATA_LENGTH];
            Buffer.BlockCopy(header, 0, packet, 0, header.Length);

            int offset = header.Length;
            packet[offset++] = 0x00; // 操作: 0:开始导航
            packet[offset++] = 0x00; // 导航方式: 0:导航到路径点
            packet[offset++] = 0x00; // 是否指定导航路径: 0:不指定
            packet[offset++] = 0x00; // 是否启用交通管理: 0:不启用

            // 路径点ID, 字符串格式, 采用 ascii 编码, 长度8字节, 不足补0
            string pointIdStr = targetPointId.ToString();
            byte[] pointIdAsciiBytes = new byte[8]; // 固定8字节
            byte[] tempAscii = Encoding.ASCII.GetBytes(pointIdStr);
            int lengthToCopy = Math.Min(tempAscii.Length, pointIdAsciiBytes.Length);
            Buffer.BlockCopy(tempAscii, 0, pointIdAsciiBytes, 0, lengthToCopy);
            // 如果 tempAscii.Length < 8,剩余部分已默认为0 (byte arrays are zero-initialized)

            Buffer.BlockCopy(pointIdAsciiBytes, 0, packet, offset, pointIdAsciiBytes.Length);
            // offset += pointIdAsciiBytes.Length; // 更新offset，虽然之后不再使用

            return packet;
        }

        /// <summary>
        /// 构建状态查询命令 (0xAF)
        /// </summary>
        private byte[] BuildStatusQueryCommand()
        {
            const byte COMMAND_CODE = 0xAF; // 查询机器人状态命令码
            const ushort DATA_LENGTH = 0;   // 此命令数据区为空
            return BuildPacketHeader(COMMAND_CODE, DATA_LENGTH);
        }
        #endregion

        #region UDP 通信 (UDP Communication)
        /// <summary>
        /// 异步发送 UDP 报文
        /// </summary>
        /// <param name="packet">要发送的报文</param>
        /// <returns>发送是否成功</returns>
        private async Task<bool> SendPacketAsync(byte[] packet)
        {
            if (_udpClient == null || _agvEndPoint == null)
            {
                Console.WriteLine("[AGVService] UDP 客户端或终结点未配置。请先调用 Configure() 和 Start()。");
                try
                {
                    Configure(); //尝试使用默认值配置
                    if(_udpClient == null || _agvEndPoint == null) 
                        throw new InvalidOperationException("UDP 客户端或终结点配置失败。");
                }
                catch (Exception ex)
                {
                     Console.WriteLine($"[AGVService] 自动配置失败: {ex.Message}");
                     return false;
                }
            }

            try
            {
                await _udpClient.SendAsync(packet, packet.Length, _agvEndPoint);
                // Console.WriteLine($"[AGVService] 已发送 {packet.Length} 字节到 {_agvEndPoint}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AGVService] 发送 UDP 报文失败: {ex.Message}");
                return false;
            }
        }
        #endregion

        #region AGV 移动控制 (AGV Motion Control) - 需求 1
        /// <summary>
        /// 简单导航命令：使AGV导航到指定的目标点ID (使用 0x16 命令)
        /// </summary>
        /// <param name="targetPointId">目标点 ID</param>
        /// <returns>命令是否成功发送</returns>
        public async Task<bool> NavigateToPointAsync(uint targetPointId)
        {
            try
            {
                byte[] packet = BuildSimpleNavigationCommand(targetPointId);
                bool success = await SendPacketAsync(packet);

                if (success)
                {
                    Console.WriteLine($"[AGVService] (使用 0x16) 导航命令已发送: 目标点ID={targetPointId}");
                    _lastCommandedTargetPointId = targetPointId;
                    _isNavigateToPointCommandActive = true; // 激活简单导航跟踪
                }
                else
                {
                    Console.WriteLine($"[AGVService] (使用 0x16) 发送导航命令失败: 目标点ID={targetPointId}");
                    _isNavigateToPointCommandActive = false;
                }
                return success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AGVService] (使用 0x16) 执行导航命令时发生错误: {ex.Message}");
                _isNavigateToPointCommandActive = false;
                return false;
            }
        }
        #endregion

        #region AGV 状态与到达判断 (AGV Status & Arrival Check) - 需求 2 & 3

        /// <summary>
        /// 异步查询 AGV 的详细状态 (电池, 速度, 位置等)。
        /// 这是获取原始状态数据并解析的内部方法。
        /// </summary>
        /// <returns>AgvStatus 对象，如果查询失败或解析失败则为 null</returns>
        private async Task<AgvStatus> QueryRawStatusAsync()
        {
            try
            {
                byte[] queryPacket = BuildStatusQueryCommand(); // 0xAF 命令
                bool sendSuccess = await SendPacketAsync(queryPacket);
                if (!sendSuccess)
                {
                    Console.WriteLine("[AGVService] 发送状态查询报文 (0xAF) 失败。");
                    return null;
                }

                // 等待响应，设置超时
                var receiveTask = _udpClient.ReceiveAsync();
                if (await Task.WhenAny(receiveTask, Task.Delay(3000)) == receiveTask) // 3秒超时
                {
                    if (receiveTask.IsFaulted || receiveTask.IsCanceled)
                    {
                        Console.WriteLine("[AGVService] 接收状态响应时发生错误或被取消。");
                        if(receiveTask.Exception != null) Console.WriteLine($"[AGVService] 异常: {receiveTask.Exception.InnerException?.Message ?? receiveTask.Exception.Message}");
                        return null;
                    }
                    
                    UdpReceiveResult result = receiveTask.Result;
                    // Console.WriteLine($"[AGVService] 收到来自 {result.RemoteEndPoint} 的 {result.Buffer.Length} 字节响应。");

                    // 校验响应报文头 (可选，但推荐)
                    // 例如检查序列号、命令码 (应为0xAF)、执行码 (应为0x00成功)
                    if (result.Buffer.Length < 28) // 通用报文头长度
                    {
                        Console.WriteLine("[AGVService] 接收到的状态响应数据过短 (小于报文头长度)。");
                        return null;
                    }
                    // byte responseCommandCode = result.Buffer[0x15];
                    // byte executionCode = result.Buffer[0x16];
                    // if (responseCommandCode != 0xAF || executionCode != 0x00) {
                    //    Console.WriteLine($"[AGVService] 状态响应报文无效: Cmd={responseCommandCode:X2}, Exec={executionCode:X2}");
                    //    return null;
                    // }

                    return ParseStatusResponse(result.Buffer); // 解析响应数据
                }
                else
                {
                    Console.WriteLine("[AGVService] 接收状态响应超时。");
                    return null;
                }
            }
            catch (SocketException se)
            {
                 Console.WriteLine($"[AGVService] 查询 AGV 状态时发生套接字错误: {se.Message} (错误码: {se.SocketErrorCode})");
                 return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AGVService] 查询 AGV 状态时发生错误: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 解析 AGV 状态响应报文 (0xAF)。
        /// </summary>
        /// <param name="responseData">收到的完整UDP原始字节数据</param>
        /// <returns>解析后的 AgvStatus 对象，如果解析失败则为 null</returns>
        private AgvStatus ParseStatusResponse(byte[] responseData)
        {
            // 0xAF 响应报文数据区定义参考文档 3.4.4 (page 16-18)
            // 通用报文头长度为 28 (0x1C) 字节
            const int commonHeaderLength = 28;
            if (responseData.Length < commonHeaderLength)
            {
                Console.WriteLine($"[AGVService] ParseStatusResponse: 响应数据过短，无法解析通用报文头。长度: {responseData.Length}");
                return null;
            }

            var status = new AgvStatus();

            try
            {
                // LocationStatusInfo: 从通用报文头后偏移 0x04 开始, 结构体大小 0x20 (32字节)
                int locInfoBaseOffset = commonHeaderLength + 0x04;
                if (responseData.Length >= locInfoBaseOffset + 16) // 需要至少16字节来读取X,Y和CurrentPointId
                {
                    status.X = BitConverter.ToSingle(responseData, locInfoBaseOffset + 0x00); // X坐标
                    status.Y = BitConverter.ToSingle(responseData, locInfoBaseOffset + 0x04); // Y坐标
                    status.CurrentPointId = BitConverter.ToUInt32(responseData, locInfoBaseOffset + 0x0C); // 最后通过点ID
                }
                else
                {
                    Console.WriteLine("[AGVService] ParseStatusResponse: 数据不足以解析完整 LocationStatusInfo。");
                }

                // RunningStatusInfo: 从通用报文头后偏移 0x24 开始, 结构体大小 0x14 (20字节)
                int runInfoBaseOffset = commonHeaderLength + 0x24;
                if (responseData.Length >= runInfoBaseOffset + 14) // 需要至少14字节来读取Vx和AgvOperationalStatus
                {
                    status.Vx = BitConverter.ToSingle(responseData, runInfoBaseOffset + 0x00); // X轴速度 (行走速度)
                    status.AgvOperationalStatus = responseData[runInfoBaseOffset + 0x0D]; // AGV状态 (0x0D是状态字段在RunningStatusInfo内的偏移)
                }
                else
                {
                    Console.WriteLine("[AGVService] ParseStatusResponse: 数据不足以解析完整 RunningStatusInfo。");
                }

                // TaskStatusInfo: 从通用报文头后偏移 0x38 开始, 长度可变
                int taskInfoBaseOffset = commonHeaderLength + 0x38;
                int taskInfoActualLength = 0;
                if (responseData.Length >= taskInfoBaseOffset + 12) // 任务状态信息头至少12字节 (OrderID, TaskKey, point_size, path_size, reserved)
                {
                    byte pointSize = responseData[taskInfoBaseOffset + 8]; // 点状态序列数量
                    byte pathSize = responseData[taskInfoBaseOffset + 9];  // 段状态序列数量
                    taskInfoActualLength = 12 + (pointSize * 8) + (pathSize * 8); // 每个序列8字节 (U32序列号, U32点/段ID)
                }
                else
                {
                     Console.WriteLine("[AGVService] ParseStatusResponse: 数据不足以解析 TaskStatusInfo 头部。");
                }
                
                // BatteryStatusInfo: 在TaskStatusInfo之后, 结构体大小 0x14 (20字节)
                // "偏移 01" 指的是紧跟在 TaskStatusInfo 之后
                int batteryInfoBaseOffset = taskInfoBaseOffset + taskInfoActualLength;
                if (taskInfoActualLength > 0 && responseData.Length >= batteryInfoBaseOffset + 4) // 需要至少4字节来读取StateOfCharge
                {
                    status.StateOfCharge = BitConverter.ToSingle(responseData, batteryInfoBaseOffset + 0x00) * 100.0f; // 电量百分比 (原始值为0-1)
                }
                else
                {
                    if(taskInfoActualLength == 0) Console.WriteLine("[AGVService] ParseStatusResponse: 因TaskStatusInfo解析问题，无法定位BatteryStatusInfo。");
                    else Console.WriteLine("[AGVService] ParseStatusResponse: 数据不足以解析 BatteryStatusInfo。");
                }
                
                return status;
            }
            catch (ArgumentOutOfRangeException aoorex)
            {
                Console.WriteLine($"[AGVService] 解析状态响应因数据长度不足而出错: {aoorex.Message}. 数据总长度: {responseData.Length}");
                // 返回部分解析的状态或null
                return status.X.HasValue || status.Vx.HasValue ? status : null; 
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AGVService] 解析状态响应时发生一般错误: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 检查 AGV 是否已到达上一个简单导航指令 (0x16) 的目标点。
        /// </summary>
        /// <returns>如果已到达返回 true, 否则返回 false。如果无法获取状态或无激活指令也返回 false。</returns>
        public async Task<bool> HasReachedTargetPointAsync()
        {
            if (!_isNavigateToPointCommandActive)
            {
                // Console.WriteLine("[AGVService] HasReachedTargetPointAsync: 当前没有简单导航指令正在监控。");
                return false; // 没有活动的导航指令，所以不能说“已到达”
            }

            AgvStatus currentStatus = await QueryRawStatusAsync();
            if (currentStatus == null)
            {
                Console.WriteLine("[AGVService] HasReachedTargetPointAsync: 查询 AGV 状态失败，无法判断是否到达。");
                return false;
            }

            // 根据文档 3.4.4 (page 17), RunningStatusInfo->状态: 0x00 表示空闲
            bool isIdle = currentStatus.AgvOperationalStatus.HasValue && currentStatus.AgvOperationalStatus.Value == 0x00;
            // LocationStatusInfo->最后通过点ID
            bool isAtTargetPointId = currentStatus.CurrentPointId.HasValue && currentStatus.CurrentPointId.Value == _lastCommandedTargetPointId;
            
            // Console.WriteLine($"[AGVService] 到达检查: Idle={isIdle}, AtPoint={isAtTargetPointId}, Target={_lastCommandedTargetPointId}, Current={currentStatus.CurrentPointId?.ToString() ?? "N/A"}, OpStatus={currentStatus.AgvOperationalStatus?.ToString("X2") ?? "N/A"}");


            if (isIdle && isAtTargetPointId)
            {
                Console.WriteLine($"[AGVService] HasReachedTargetPointAsync: AGV 已到达目标点 ID: {_lastCommandedTargetPointId} 且状态为空闲。");
                _isNavigateToPointCommandActive = false; // 重置标志，因为任务完成
                return true;
            }
            
            // 如果AGV不在目标点，或者不在空闲状态，则认为未最终到达
            return false;
        }

        /// <summary>
        /// 公共方法：查询 AGV 的诊断信息 (电池百分比, 速度, 位置)。
        /// </summary>
        /// <returns>AgvStatus 对象包含请求的信息。如果查询失败，则相关属性可能为 null。</returns>
        public async Task<AgvStatus> QueryDetailedStatusAsync()
        {
            AgvStatus status = await QueryRawStatusAsync();
            if (status == null)
            {
                Console.WriteLine("[AGVService] QueryDetailedStatusAsync: 获取 AGV 详细状态失败。返回一个包含null值的状态对象。");
                return new AgvStatus(); // 返回一个空/默认的AgvStatus对象，所有属性为null
            }
            return status;
        }

        #endregion

        // 保留用户提供的 SendCommandToAGV 方法，因为它使用了 NavigateToPointAsync
        // 如果有其他命令类型，可以在这里扩展
        /// <summary>
        /// 发送 AGV 指令 (简化版本，目前主要通过 NavigateToPointAsync 实现导航)
        /// </summary>
        /// <param name="agvId">AGV 标识符 (当前未使用，因为直接与配置的AGV通信)</param>
        /// <param name="command">指令类型，例如 "GOTOTARGETPOINT"</param>
        /// <param name="parameters">包含指令参数的字典, 例如 {"TargetPointId", 123u}</param>
        /// <returns>是否成功发送指令</returns>
        public async Task<bool> SendCommandToAGVAsync(string agvId, string command, Dictionary<string, object> parameters)
        {
            // agvId 当前未使用，因为服务配置为与单个AGV通信。
            // 如果需要多AGV支持，则需要修改Configure和内部逻辑。
            try
            {
                if (string.IsNullOrEmpty(command))
                {
                    throw new ArgumentException("指令类型不能为空。");
                }

                switch (command.ToUpper())
                {
                    case "GOTOTARGETPOINT": // 自定义一个命令类型用于简单导航
                        if (parameters != null && parameters.TryGetValue("TargetPointId", out object pointIdObj) && pointIdObj is uint pointId)
                        {
                            Console.WriteLine($"[AGVService] 指令 AGV ({agvId}) 前往目标点: ID={pointId}");
                            return await NavigateToPointAsync(pointId);
                        }
                        else
                        {
                            Console.WriteLine($"[AGVService] GOTOTARGETPOINT 命令缺少有效的 TargetPointId (uint) 参数。");
                            return false;
                        }
                    // 可以根据需要添加其他命令，例如使用0xAE的复杂导航或0xB2的立即动作
                    // case "GOTOSWITCHPOINT": 
                    // case "GOTOWAITPOINT":
                    //    // 这些可以映射到 NavigateToPointAsync 或其他构建命令的方法
                    //    uint targetId = ...; // 从 parameters 中解析
                    //    return await NavigateToPointAsync(targetId);

                    default:
                        Console.WriteLine($"[AGVService] 不支持的指令类型: {command}");
                        return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AGVService] 发送 AGV 指令失败 (AGV ID: {agvId}, Command: {command}): {ex.Message}");
                return false;
            }
        }
    }
}
