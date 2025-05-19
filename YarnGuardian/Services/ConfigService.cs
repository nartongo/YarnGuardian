using System;
using System.IO;
using System.Text.Json;

namespace YarnGuardian.Services
{
    /// <summary>
    /// 简单的配置服务，用于读取和保存配置
    /// </summary>
    public class ConfigService
    {
        // 单例实例
        private static ConfigService _instance;
        
        // 配置文件路径
        private static readonly string ConfigFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        
        // 默认配置
        private static readonly Config DefaultConfig = new Config
        {
            PLCIPAddress = "192.168.0.1",
            PLCPort = 502,
            PLCSlaveId = 17,
            AGVIPAddress = "192.168.100.178",
            AGVPort = 17804,
            AutoStart = false,
            LogLevel = "Info",
            MySqlServer = "localhost",
            MySqlPort = 3306,
            MySqlDatabase = "yarn_guardian",
            MySqlUsername = "remote",
            MySqlPassword = "root",
            MachineId = "MACHINE001",
            ZeroMqPublishAddress = "tcp://localhost:5555",
            ZeroMqSubscribeAddress = "tcp://localhost:5556"
        };
        
        // 当前配置
        private Config _config;
        
        /// <summary>
        /// 获取单例实例
        /// </summary>
        public static ConfigService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ConfigService();
                }
                return _instance;
            }
        }
        
        /// <summary>
        /// 构造函数，初始化配置
        /// </summary>
        public ConfigService()
        {
            LoadConfig();
        }
        
        /// <summary>
        /// 获取PLC IP地址
        /// </summary>
        public string GetPlcIpAddress()
        {
            return _config.PLCIPAddress;
        }
        
        /// <summary>
        /// 获取PLC端口
        /// </summary>
        public int GetPlcPort()
        {
            return _config.PLCPort;
        }
        
        /// <summary>
        /// 获取从站ID
        /// </summary>
        public int GetPlcSlaveId()
        {
            return _config.PLCSlaveId;
        }
        
        /// <summary>
        /// 设置PLC IP地址
        /// </summary>
        public void SetPlcIpAddress(string ipAddress)
        {
            _config.PLCIPAddress = ipAddress;
        }
        
        /// <summary>
        /// 设置PLC端口
        /// </summary>
        public void SetPlcPort(int port)
        {
            _config.PLCPort = port;
        }
        
        /// <summary>
        /// 设置从站ID
        /// </summary>
        public void SetPlcSlaveId(int slaveId)
        {
            _config.PLCSlaveId = slaveId;
        }
        
        /// <summary>
        /// 获取AGV IP地址
        /// </summary>
        public string GetAgvIpAddress()
        {
            return _config.AGVIPAddress;
        }
        
        /// <summary>
        /// 获取AGV端口
        /// </summary>
        public int GetAgvPort()
        {
            return _config.AGVPort;
        }
        
        /// <summary>
        /// 设置AGV IP地址
        /// </summary>
        public void SetAgvIpAddress(string ipAddress)
        {
            _config.AGVIPAddress = ipAddress;
        }
        
        /// <summary>
        /// 设置AGV端口
        /// </summary>
        public void SetAgvPort(int port)
        {
            _config.AGVPort = port;
        }
        
        /// <summary>
        /// 获取MySQL服务器地址
        /// </summary>
        public string GetMySqlServer()
        {
            return _config.MySqlServer;
        }
        
        /// <summary>
        /// 获取MySQL端口
        /// </summary>
        public int GetMySqlPort()
        {
            return _config.MySqlPort;
        }
        
        /// <summary>
        /// 获取MySQL数据库名
        /// </summary>
        public string GetMySqlDatabase()
        {
            return _config.MySqlDatabase;
        }
        
        /// <summary>
        /// 获取MySQL用户名
        /// </summary>
        public string GetMySqlUsername()
        {
            return _config.MySqlUsername;
        }
        
        /// <summary>
        /// 获取MySQL密码
        /// </summary>
        public string GetMySqlPassword()
        {
            return _config.MySqlPassword;
        }
        
        /// <summary>
        /// 设置MySQL服务器地址
        /// </summary>
        public void SetMySqlServer(string server)
        {
            _config.MySqlServer = server;
        }
        
        /// <summary>
        /// 设置MySQL端口
        /// </summary>
        public void SetMySqlPort(int port)
        {
            _config.MySqlPort = port;
        }
        
        /// <summary>
        /// 设置MySQL数据库名
        /// </summary>
        public void SetMySqlDatabase(string database)
        {
            _config.MySqlDatabase = database;
        }
        
        /// <summary>
        /// 设置MySQL用户名
        /// </summary>
        public void SetMySqlUsername(string username)
        {
            _config.MySqlUsername = username;
        }
        
        /// <summary>
        /// 设置MySQL密码
        /// </summary>
        public void SetMySqlPassword(string password)
        {
            _config.MySqlPassword = password;
        }
        
        /// <summary>
        /// 获取机器ID
        /// </summary>
        public string GetMachineId()
        {
            return _config.MachineId;
        }
        
        /// <summary>
        /// 设置机器ID
        /// </summary>
        public void SetMachineId(string id)
        {
            _config.MachineId = id;
        }
        
        /// <summary>
        /// 获取ZeroMQ发布地址
        /// </summary>
        public string GetZeroMqPublishAddress()
        {
            return _config.ZeroMqPublishAddress;
        }
        
        /// <summary>
        /// 设置ZeroMQ发布地址
        /// </summary>
        public void SetZeroMqPublishAddress(string addr)
        {
            _config.ZeroMqPublishAddress = addr;
        }
        
        /// <summary>
        /// 获取ZeroMQ订阅地址
        /// </summary>
        public string GetZeroMqSubscribeAddress()
        {
            return _config.ZeroMqSubscribeAddress;
        }
        
        /// <summary>
        /// 设置ZeroMQ订阅地址
        /// </summary>
        public void SetZeroMqSubscribeAddress(string addr)
        {
            _config.ZeroMqSubscribeAddress = addr;
        }
        
        /// <summary>
        /// 获取奇数边号断头排序方式
        /// </summary>
        public string GetBreakPointSortOddSide()
        {
            return _config.BreakPointSortOddSide;
        }
        
        /// <summary>
        /// 获取偶数边号断头排序方式
        /// </summary>
        public string GetBreakPointSortEvenSide()
        {
            return _config.BreakPointSortEvenSide;
        }
        
        /// <summary>
        /// 设置奇数边号断头排序方式
        /// </summary>
        public void SetBreakPointSortOddSide(string sort)
        {
            _config.BreakPointSortOddSide = sort;
        }
        
        /// <summary>
        /// 设置偶数边号断头排序方式
        /// </summary>
        public void SetBreakPointSortEvenSide(string sort)
        {
            _config.BreakPointSortEvenSide = sort;
        }
        
        /// <summary>
        /// 加载配置文件
        /// </summary>
        public void LoadConfig()
        {
            try
            {
                // 如果配置文件存在，则加载它
                if (File.Exists(ConfigFilePath))
                {
                    string json = File.ReadAllText(ConfigFilePath);
                    _config = JsonSerializer.Deserialize<Config>(json);
                    Console.WriteLine("配置已加载");
                }
                else
                {
                    // 如果配置文件不存在，则使用默认配置并创建文件
                    _config = DefaultConfig;
                    SaveConfig();
                    Console.WriteLine("已创建默认配置文件");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载配置出错: {ex.Message}");
                _config = DefaultConfig;
            }
        }
        
        /// <summary>
        /// 保存配置到文件
        /// </summary>
        public void SaveConfig()
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                
                string json = JsonSerializer.Serialize(_config, options);
                File.WriteAllText(ConfigFilePath, json);
                Console.WriteLine("配置已保存");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存配置出错: {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// 配置类，定义所有可配置项
    /// </summary>
    public class Config
    {
        // PLC设置
        public string PLCIPAddress { get; set; }
        public int PLCPort { get; set; }
        public int PLCSlaveId { get; set; }
        
        // AGV设置
        public string AGVIPAddress { get; set; }
        public int AGVPort { get; set; }
        
        // MySQL数据库设置
        public string MySqlServer { get; set; }
        public int MySqlPort { get; set; }
        public string MySqlDatabase { get; set; }
        public string MySqlUsername { get; set; }
        public string MySqlPassword { get; set; }
        
        // ZeroMQ相关
        public string MachineId { get; set; }
        public string ZeroMqPublishAddress { get; set; }
        public string ZeroMqSubscribeAddress { get; set; }
        
        // 系统设置
        public bool AutoStart { get; set; }
        public string LogLevel { get; set; }
        
        // 断头排序规则
        public string BreakPointSortOddSide { get; set; } = "Asc";   // "Asc" 或 "Desc"
        public string BreakPointSortEvenSide { get; set; } = "Desc"; // "Asc" 或 "Desc"
        
        // 可根据需要添加其他配置项...
    }
}
