using System;
using System.Threading.Tasks;
using YarnGuardian.Common; 
using YarnGuardian.Services; 

namespace YarnGuardian.Coordinator
{
    public class MainCoordinator
    {
        private readonly AGVService _agvService;
        private readonly PLCService _plcService;
        //  MySqlDataService, ConfigService 
        private readonly MySqlDataService _mySqlDataService;
        private readonly ConfigService _configService;
        // private readonly ZeroMqClient _zeroMqClient;


        // 事件名称常量 (确保与发布者一致)
        
        // 例如: public static class SystemEvents { public const string StartClicked = "EVENT_START"; ... }
        private const string EVENT_START_CLICKED = "EVENT_START"; 
        private const string EVENT_STOP_CLICKED = "EVENT_STOP";   

        // 构造函数：注入依赖的服务
        public MainCoordinator(
            AGVService agvService,
            PLCService plcService,
            MySqlDataService mySqlDataService,
            ConfigService configService
            // ZeroMqClient zeroMqClient
            )
        {
            _agvService = agvService ?? throw new ArgumentNullException(nameof(agvService));
            _plcService = plcService ?? throw new ArgumentNullException(nameof(plcService));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _mySqlDataService = mySqlDataService ?? throw new ArgumentNullException(nameof(mySqlDataService));
            
            // _zeroMqClient = zeroMqClient ?? throw new ArgumentNullException(nameof(zeroMqClient));

            SubscribeToSystemEvents();
        }

        private void SubscribeToSystemEvents()
        {
            // 从 EventHub 订阅启动按钮点击事件
            EventHub.Instance.Subscribe(EVENT_START_CLICKED, HandleStartButtonClicked);
            EventHub.Instance.Subscribe(EVENT_STOP_CLICKED, HandleStopButtonClicked);
            Console.WriteLine("[MainCoordinator] 已订阅系统启动和停止事件。");
        }

        private async void HandleStartButtonClicked(object payload)
        {
            // payload 通常是事件发布时传递的数据，这里是 DateTime
            if (payload is DateTime startTime)
            {
                Console.WriteLine($"[MainCoordinator] 接收到启动事件，触发时间: {startTime}. 开始初始化系统...");
            }
            else
            {
                Console.WriteLine("[MainCoordinator] 接收到启动事件 (无详细时间信息). 开始初始化系统...");
            }

            try
            {
                // 1. 配置和启动 AGV 服务
                string agvIp = _configService.GetAgvIpAddress();
                int agvPort = _configService.GetAgvPort();
                _agvService.Configure(agvIp, agvPort);

                _agvService.Configure(); // 使用 AGVService 内的默认值
                //_agvService.Start(); start也是调用了configure

                // 2. 连接 PLC 服务
                string plcIp = _configService.GetPlcIpAddress();
                int plcPort = _configService.GetPlcPort();
                _plcService.Configure(plcIp, plcPort);
                bool plcConnected = await _plcService.ConnectAsync();

                if (!plcConnected)
                {
                    Console.WriteLine("[MainCoordinator] PLC服务连接失败。");
                    return;
                }

                // 3. 连接mysql数据库
                bool mysqlConnected = await _mySqlDataService.ConnectAsync();
                if (!mysqlConnected)
                {
                    Console.WriteLine("[MainCoordinator] MySQL数据库连接失败。");
                    return;
                }

                // 4. 连接ZeroMqClient
                ZeroMqClient zeroMqClient = new ZeroMqClient(_configService);
                if (!zeroMqClient.Connect())
                {
                    Console.WriteLine("ZeroMQ 连接失败，请检查配置或网络。");
                    return;
                }

                Console.WriteLine("ZeroMQ 连接成功，可以进行后续通信。");
                zeroMqClient.SendStartRequest();
                StartStatusReporting(zeroMqClient, 30000); // 每30秒上报一次
                
               

                // TODO: 添加其他必要的初始化逻辑

                Console.WriteLine("[MainCoordinator] 系统初始化流程完成。");
                EventHub.Instance.Publish("SYSTEM_INITIALIZED_SUCCESS", null); // 可以发布一个系统初始化成功的事件
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainCoordinator] 处理启动事件时发生错误: {ex.Message}");
                EventHub.Instance.Publish("SYSTEM_INITIALIZATION_FAILED", ex.Message); // 发布初始化失败事件
                // 可能需要回滚已启动的服务
            }
        }

        private async void HandleStopButtonClicked(object payload)
        {
            if (payload is DateTime stopTime)
            {
                Console.WriteLine($"[MainCoordinator] 接收到停止事件，触发时间: {stopTime}. 开始关闭系统...");
            }
            else
            {
                Console.WriteLine("[MainCoordinator] 接收到停止事件. 开始关闭系统...");
            }

            try
            {
                _agvService.Stop();
                Console.WriteLine("[MainCoordinator] AGV服务已停止。");

                await _plcService.DisconnectAsync();
                Console.WriteLine("[MainCoordinator] PLC服务已断开。");

                // _zeroMqClient.Disconnect();
                // Console.WriteLine("[MainCoordinator] ZeroMQ客户端已断开。");

                // _mySqlDataService.Disconnect(); // 或者 _sqliteDataService
                // Console.WriteLine("[MainCoordinator] 数据库服务已断开。");

                // TODO: 添加其他必要的关闭逻辑

                Console.WriteLine("[MainCoordinator] 系统关闭流程完成。");
                EventHub.Instance.Publish("SYSTEM_SHUTDOWN_COMPLETE", null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainCoordinator] 处理停止事件时发生错误: {ex.Message}");
                // 即使出错，也应尝试关闭所有能关闭的资源
            }
        }



        // * 状态上报相关代码

        //采集状态 agv D500的值  agv 的状态 然后定时上报
        private async Task CollectAndReportStatusAsync(ZeroMqClient zeroMqClient)
        {
            // 1. 查询AGV状态
            AgvStatus agvStatus = await _agvService.QueryDetailedStatusAsync();

            // 2. 查询D500（锭子距离）float值
            float spindlePosition = await _plcService.GetSpindlePositionAsync();

            // 3. 打包成status对象
            var status = new
            {
                spindlePosition,
                agv = agvStatus,
                timestamp = DateTime.UtcNow.ToString("o")
            };

            // 4. 上报
            zeroMqClient.SendStatusReport(status);
        }



        private CancellationTokenSource _statusReportCts;

        //开始状态上报
        public void StartStatusReporting(ZeroMqClient zeroMqClient, int intervalMs = 1000)
        {
            _statusReportCts = new CancellationTokenSource();
            Task.Run(async () =>
            {
                while (!_statusReportCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await CollectAndReportStatusAsync(zeroMqClient);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[MainCoordinator] 状态上报出错: {ex.Message}");
                    }
                    await Task.Delay(intervalMs, _statusReportCts.Token);
                }
            }, _statusReportCts.Token);
        }

        //停止状态上报
        public void StopStatusReporting()
        {
            _statusReportCts?.Cancel();
        }



        // 建议在应用程序关闭时取消订阅，以防止内存泄漏
        public void UnsubscribeSystemEvents()
        {
            EventHub.Instance.Unsubscribe(EVENT_START_CLICKED, HandleStartButtonClicked);
            EventHub.Instance.Unsubscribe(EVENT_STOP_CLICKED, HandleStopButtonClicked);
            Console.WriteLine("[MainCoordinator] 已取消订阅系统事件。");
        }
    }
}
