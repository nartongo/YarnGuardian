using System;
using System.Threading.Tasks;
using EasyModbus; // 尝试这个命名空间
using YarnGuardian.Common;

namespace YarnGuardian.Services
{
    public class PLCService
    {
        private readonly EventHub _eventHub;
        private ModbusClient _modbusClient;
        private bool _isConnected;
        
        // PLC地址定义和约束
        private const int MAX_M_ADDRESS = 7999; // M地址最大值
        private const int MAX_D_ADDRESS = 7999; // D地址最大值
        private const byte DEFAULT_SLAVE_ID = 17; // 默认从站ID
        
        // 常用PLC地址名称定义
        public static class PLCAddresses
        {
            // 线圈地址
            public const string SWITCH_POINT_ARRIVED = "M500"; // 告知PLC到达切换点信号
            public const string TRIGGER_ROLLERS = "M501";      // 左右皮辊信号
            public const string SPINDLE_ARRIVAL = "M600";      // PLC到达锭位信号
            public const string REPAIR_DONE = "M601";          // 接头完成信号
            public const string BACK_TO_SWITCH_POINT = "M602"; // PLC告知到达权限切换点
            
            // 寄存器地址
            public const string SPINDLE_POSITION = "D500";     // 锭子距离寄存器地址
        }
        
        public PLCService()
        {
            _eventHub = EventHub.Instance;
            _modbusClient = new ModbusClient();
            _modbusClient.UnitIdentifier = DEFAULT_SLAVE_ID; // 设置从站ID
            _modbusClient.ConnectionTimeout = 3000;          // 设置连接超时(3秒)
        }

        /// <summary>
        /// 配置PLC连接参数
        /// </summary>
        /// <param name="ipAddress">PLC IP地址</param>
        /// <param name="port">PLC端口</param>
        /// <param name="slaveId">从站ID，默认为17</param>
        public void Configure(string ipAddress, int port = 502, byte slaveId = DEFAULT_SLAVE_ID)
        {
            _modbusClient.IPAddress = ipAddress;
            _modbusClient.Port = port;
            _modbusClient.UnitIdentifier = slaveId;
            
            Console.WriteLine($"[PLCService] 已配置PLC连接参数: IP={ipAddress}, Port={port}, SlaveID={slaveId}");
        }
        
        /// <summary>
        /// 连接到PLC
        /// </summary>
        public async Task<bool> ConnectAsync()
        {
            try
            {
                if (_modbusClient.Connected)
                {
                    _isConnected = true;
                    return true;
                }
                    
                await Task.Run(() => _modbusClient.Connect());
                
                _isConnected = true;
                Console.WriteLine("[PLCService] 已连接到PLC");
                return true;
            }
            catch (Exception ex)
            {
                _isConnected = false;
                Console.WriteLine($"[PLCService] 连接PLC失败: {ex.Message}");
                return false;
            }
        }
        
        
        /// <summary>
        /// 断开PLC连接
        /// </summary>
        public async Task DisconnectAsync()
        {
            try
            {
                if (!_modbusClient.Connected)
                    return;
                    
                // 在后台线程中执行断开连接操作
                await Task.Run(() => {
                    _modbusClient.Disconnect();
                });
                
                _isConnected = false;
                Console.WriteLine("[PLCService] 已断开PLC连接");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PLCService] 断开PLC连接失败: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 解析PLC地址为线圈索引
        /// </summary>
        private int ParseCoilAddress(string address)
        {
            if (string.IsNullOrEmpty(address))
                throw new ArgumentException("PLC地址不能为空");
                
            if (address.StartsWith("M", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(address.Substring(1), out int coilIndex) && coilIndex <= MAX_M_ADDRESS)
                {
                    return coilIndex;
                }
            }
            
            throw new ArgumentException($"无效的PLC线圈地址: {address}");
        }
        
        /// <summary>
        /// 解析PLC地址为寄存器索引
        /// </summary>
        private int ParseRegisterAddress(string address)
        {
            if (string.IsNullOrEmpty(address))
                throw new ArgumentException("PLC地址不能为空");
                
            if (address.StartsWith("D", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(address.Substring(1), out int registerIndex) && registerIndex <= MAX_D_ADDRESS)
                {
                    return registerIndex;
                }
            }
            
            throw new ArgumentException($"无效的PLC寄存器地址: {address}");
        }
        
        /// <summary>
        /// 异步读取线圈状态
        /// </summary>
        /// <param name="address">PLC线圈地址，如"M500"</param>
        /// <returns>线圈状态</returns>
        public async Task<bool> ReadCoilAsync(string address)
        {
            if (!_isConnected)
                throw new InvalidOperationException("PLC未连接");
                
            int coilIndex = ParseCoilAddress(address);
            
            // 在后台线程中执行同步读取操作
            return await Task.Run(() => {
                bool[] coils = _modbusClient.ReadCoils(coilIndex, 1);
                return coils[0];
            });
        }
        
        /// <summary>
        /// 异步写入线圈状态
        /// </summary>
        /// <param name="address">PLC线圈地址，如"M500"</param>
        /// <param name="value">要写入的状态值</param>
        public async Task WriteCoilAsync(string address, bool value)
        {
            if (!_isConnected)
                throw new InvalidOperationException("PLC未连接");
                
            int coilIndex = ParseCoilAddress(address);
            
            // 在后台线程中执行同步写入操作
            await Task.Run(() => {
                _modbusClient.WriteSingleCoil(coilIndex, value);
            });
        }
        
        /// <summary>
        /// 异步读取寄存器值
        /// </summary>
        /// <param name="address">PLC寄存器地址，如"D500"</param>
        /// <returns>寄存器值</returns>
        public async Task<int> ReadRegisterAsync(string address)
        {
            if (!_isConnected)
                throw new InvalidOperationException("PLC未连接");
                
            int registerIndex = ParseRegisterAddress(address);
            
            // 在后台线程中执行同步读取操作
            return await Task.Run(() => {
                int[] registers = _modbusClient.ReadHoldingRegisters(registerIndex, 1);
                return registers[0];
            });
        }
        
        /// <summary>
        /// 异步写入寄存器值
        /// </summary>
        /// <param name="address">PLC寄存器地址，如"D500"</param>
        /// <param name="value">要写入的值</param>
        public async Task WriteRegisterAsync(string address, int value)
        {
            if (!_isConnected)
                throw new InvalidOperationException("PLC未连接");
                
            int registerIndex = ParseRegisterAddress(address);
            
            // 在后台线程中执行同步写入操作
            await Task.Run(() => {
                _modbusClient.WriteSingleRegister(registerIndex, value);
            });
        }
        
        /// <summary>
        /// 读取指定寄存器（如D500）的浮点数值（float，32位，两个寄存器）
        /// </summary>
        /// <param name="address">寄存器地址，如"D500"</param>
        /// <returns>寄存器中的float值</returns>
        public async Task<float> ReadRegisterFloatAsync(string address)
        {
            if (!_isConnected)
                throw new InvalidOperationException("PLC未连接");

            int registerIndex = ParseRegisterAddress(address);
            // 读取两个寄存器
            int[] registers = await Task.Run(() => _modbusClient.ReadHoldingRegisters(registerIndex, 2));
            // 合成float（Modbus高字在前，低字在后，通常为Big Endian）
            byte[] bytes = new byte[4];
            bytes[0] = (byte)(registers[1] & 0xFF);
            bytes[1] = (byte)((registers[1] >> 8) & 0xFF);
            bytes[2] = (byte)(registers[0] & 0xFF);
            bytes[3] = (byte)((registers[0] >> 8) & 0xFF);
            return BitConverter.ToSingle(bytes, 0);
        }

        /// <summary>
        /// 写入float值到指定寄存器（如D500，写入D500-D501）
        /// </summary>
        /// <param name="address">寄存器地址，如"D500"</param>
        /// <param name="value">要写入的float值</param>
        public async Task WriteRegisterFloatAsync(string address, float value)
        {
            if (!_isConnected)
                throw new InvalidOperationException("PLC未连接");

            int registerIndex = ParseRegisterAddress(address);
            byte[] bytes = BitConverter.GetBytes(value);
            // 组装为两个16位寄存器（注意字节序，通常Modbus为Big Endian）
            int reg0 = (bytes[3] << 8) | bytes[2]; // 高16位
            int reg1 = (bytes[1] << 8) | bytes[0]; // 低16位
            int[] regs = new int[] { reg0, reg1 };
            await Task.Run(() => _modbusClient.WriteMultipleRegisters(registerIndex, regs));
        }

        /// <summary>
        /// 获取锭子距离寄存器（D500）的float值
        /// </summary>
        /// <returns>寄存器float值</returns>
        public async Task<float> GetSpindlePositionAsync()
        {
            return await ReadRegisterFloatAsync(PLCAddresses.SPINDLE_POSITION);
        }
    }
}
