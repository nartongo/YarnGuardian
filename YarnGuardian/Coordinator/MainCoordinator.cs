using YarnGuardian.Common;
using YarnGuardian.Services;
using YarnGuardian.MQHandler;
using YarnGuardian.Server;
using YarnGuardian.Common;
namespace YarnGuardian.Coordinator
{
    public class MainCoordinator
    {
        private readonly AGVService _agvService;
        private readonly PLCService _plcService;
        //  MySqlDataService, ConfigService 
        private readonly MySqlDataService _mySqlDataService;
        private readonly ConfigService _configService;
        private readonly ZeroMqClient _zeroMqClient;
        private readonly TaskController _taskController;
        private readonly SQLiteDataService _sqliteDataService;

        // 事件名称常量 (确保与发布者一致)
        
        // 例如: public static class SystemEvents { public const string StartClicked = "EVENT_START"; ... }
        private const string EVENT_START_CLICKED = "EVENT_START"; 
        private const string EVENT_STOP_CLICKED = "EVENT_STOP";   


        private void SubscribeToBackendCommands()
        {
            EventHub.Instance.Subscribe("BackendCommandReceived", async (eventData) => await HandleBackendCommandReceived(eventData));
        }

        // 构造函数：注入依赖的服务
        public MainCoordinator(
            AGVService agvService,
            PLCService plcService,
            MySqlDataService mySqlDataService,
            ConfigService configService,
            ZeroMqClient zeroMqClient,
            TaskController taskController,
            SQLiteDataService sqliteDataService
            )
        {
            _agvService = agvService ?? throw new ArgumentNullException(nameof(agvService));
            _plcService = plcService ?? throw new ArgumentNullException(nameof(plcService));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _mySqlDataService = mySqlDataService ?? throw new ArgumentNullException(nameof(mySqlDataService));
            _taskController = taskController ?? throw new ArgumentNullException(nameof(taskController));
            _zeroMqClient = zeroMqClient ?? throw new ArgumentNullException(nameof(zeroMqClient));
            _sqliteDataService = sqliteDataService ?? throw new ArgumentNullException(nameof(sqliteDataService));

            SubscribeToSystemEvents();
            SubscribeToBackendCommands();
        }

        private void SubscribeToSystemEvents()
        {
            // 从 EventHub 订阅启动按钮点击事件
            EventHub.Instance.Subscribe(EVENT_START_CLICKED, HandleStartButtonClicked);
            EventHub.Instance.Subscribe(EVENT_STOP_CLICKED, HandleStopButtonClicked);
            Console.WriteLine("[MainCoordinator] 已订阅系统启动和停止事件。");
        }


        //后端任务传过来的细纱机
        private async Task HandleBackendCommandReceived(object eventData)
        {
            // eventData 是 ZeroMqClient 里发布的匿名对象
            try
            {
                // 用 dynamic 解析
                dynamic data = eventData;
                string type = data.Type;
                var sideNumber = data.SideNumber;
                var taskId = data.TaskId;
                var originalMessage = data.OriginalMessage;

                Console.WriteLine($"[MainCoordinator] 收到后端命令: Type={type}, SideNumber={sideNumber}, TaskId={taskId}");

                // 根据type 做不同处理
                if (type == "StartRepairTask")
                {
                    // 1. 控制agv移动到权限切换点
                    await _taskController.ExecuteRepairTaskAsync(data, _agvService, _plcService);
                    // 2. 缓存该边的码值到SQLite
                    float[] values = await _mySqlDataService.GetDistanceValuesBySideNumberAsync(sideNumber.ToString());
                    await _sqliteDataService.ReplaceSpindleDistanceValuesAsync(sideNumber.ToString(), values);
                    //2. 获取并缓存该边断头数据
                    //int[] breakPointData = await _taskController.GetBreakPointData(data);


                    //2. 执行改边断头处理完整流程
                    await _taskController.ProcessAllBreakPointsAsync(data, _plcService, _sqliteDataService, _configService);
                    //3. 告知PLC 断头处理完成，开始掉头。
                    await _plcService.TurnBackAsync();
                    // 4.等待反馈掉头完成
                    while (true)
                    {
                        bool turnBackFeedback = await _plcService.ReadCoilAsync(PLCService.PLCAddresses.TURN_BACK_FEEDBACK);
                        if (turnBackFeedback)
                        {
                            Console.WriteLine("[MainCoordinator] PLC反馈掉头完成。");
                            break;
                        }
                        else
                        {
                            Console.WriteLine("[MainCoordinator] 等待PLC反馈掉头完成...");
                            await Task.Delay(2000);
                        }
                    }
                    // 5. 缓存下一边码值到数据库
                    float[] nextSideValues = await _mySqlDataService.GetDistanceValuesBySideNumberAsync((sideNumber + 1).ToString());
                    await _sqliteDataService.ReplaceSpindleDistanceValuesAsync((sideNumber + 1).ToString(), nextSideValues);

                    //6. 下一边的断头进行处理
                    data.side_number = sideNumber + 1; // 这里修改了 边号后面的边号都是传入的边号值+1
                    await _taskController.ProcessAllBreakPointsAsync(data, _plcService, _sqliteDataService, _configService);
                    //7. 告知plc 前往权限切换点
                    await _plcService.GoToSwitchPointAsync();
                    //8. 告知PLC 切换点码值
                    await _taskController.WriteSwitchPointValueAsync(sideNumber, 001, _plcService, _sqliteDataService);
                    // 9. 查找等待点
                    string waitPointId = await _mySqlDataService.GetWaitPointIdByMachineIdAsync();
                    //10. 控制agv 前往等待点
                    uint waitPointIdUint = uint.Parse(waitPointId);
                    await _agvService.NavigateToPointAsync(waitPointIdUint);
                    //11. 等待agv 到达等待点
                    while (true)
                    {
                        bool arrived = await _agvService.HasReachedTargetPointAsync();
                        if (arrived)
                        {
                            Console.WriteLine("[MainCoordinator] AGV已到达等待点");
                            break;
                        }
                        Console.WriteLine("[MainCoordinator] 等待AGV到达等待点...");
                        await Task.Delay(2000);
                    }
                    

                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainCoordinator] 解析后端命令出错: {ex.Message}");
            }
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
                // ZeroMqClient zeroMqClient = new ZeroMqClient(_configService);
                // if (!zeroMqClient.Connect())
                // {
                //     Console.WriteLine("ZeroMQ 连接失败，请检查配置或网络。");
                //     return;
                // }

                // Console.WriteLine("ZeroMQ 连接成功，可以进行后续通信。");
                // zeroMqClient.SendStartRequest();
                // StartStatusReporting(zeroMqClient, 30000); // 每30秒上报一次
                
               

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
        private async Task CollectAndReportStatusAsync()
        {
            // 1. 查询AGV状态
            AgvStatus agvStatus = await _agvService.QueryDetailedStatusAsync();

            // 2. 查询D500（锭子距离）float值
            float spindlePosition = await _plcService.GetSpindlePositionAsync();

            // 3. 打包成status对象
            // var status = new
            // {
            //     spindlePosition,
            //     agv = agvStatus,
            //     timestamp = DateTime.UtcNow.ToString("o")
            // };
            
            var status = new StatusReportModel()
            {
                RobotId = _configService.GetMachineId(),
                Power =  agvStatus.StateOfCharge ?? 0,
                Status = "working",
                TimeStamp = DateTime.UtcNow
            };

            var msg = new MQMsg<StatusReportModel>()
            {
                Module = "agv",
                Service = "status_report",
                Content = status
            };

            // 4. 上报
           // zeroMqClient.SendStatusReport(status);
           MQApi.Instance.BackgroundSend(msg);
        }



        private CancellationTokenSource _statusReportCts;

        //开始状态上报
        public void StartStatusReporting(int intervalMs = 1000)
        {
            _statusReportCts = new CancellationTokenSource();
            Task.Run(async () =>
            {
                while (!_statusReportCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await CollectAndReportStatusAsync();
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
        
        
        public async Task MainCoordinatorDispatch<T>(MsgHandler session, MQMsg<T> msg)
        {
            try
            {
                string service = msg.Service;
                switch (service)
                {
                    case "get_schedule":
                        StopStatusReporting();
                        break;
                    case "start_repair_task":
                    // 处理断头任务, 先反序列化
                        var repairTask = System.Text.Json.JsonSerializer.Deserialize<RepairTaskMsg>(System.Text.Json.JsonSerializer.Serialize(msg.Content));
                        await ExecuteRepairTaskWorkflow(repairTask);
                        break;
                    case "status_report":
                        await CollectAndReportStatusAsync();
                        break;
                    default:
                        MQApi.SetTransferResult(1, "Unexpected Service! [" + service + "]");
                        break;
                }
            }
            catch (Exception ex)
            {
                MQApi.SetTransferResult(1, ex.Message);
            }
        }


        //方法重载
        private async Task ExecuteRepairTaskWorkflow(RepairTaskMsg taskData)
        {
            try
            {
                int sideNumber = taskData.SideNumber;
                Console.WriteLine($"[MainCoordinator] 收到断头任务: SideNumber={taskData.SideNumber}, TaskId={taskData.TaskId}");
                // 1. 控制agv移动到权限切换点
                await _taskController.ExecuteRepairTaskAsync(taskData, _agvService, _plcService);
                // 2. 缓存该边的码值到SQLite
                float[] values = await _mySqlDataService.GetDistanceValuesBySideNumberAsync(taskData.SideNumber.ToString());
                await _sqliteDataService.ReplaceSpindleDistanceValuesAsync(taskData.SideNumber.ToString(), values);
                // 3. 执行该边断头处理完整流程
                await _taskController.ProcessAllBreakPointsAsync(taskData, _plcService, _sqliteDataService, _configService);
                //3. 告知PLC 断头处理完成，开始掉头。
                await _plcService.TurnBackAsync();
                // 4.等待反馈掉头完成
                while (true)
                {
                    bool turnBackFeedback = await _plcService.ReadCoilAsync(PLCService.PLCAddresses.TURN_BACK_FEEDBACK);
                    if (turnBackFeedback)
                    {
                        Console.WriteLine("[MainCoordinator] PLC反馈掉头完成。");
                        break;
                    }
                    else
                    {
                        Console.WriteLine("[MainCoordinator] 等待PLC反馈掉头完成...");
                        await Task.Delay(2000);
                    }
                }
                // 5. 缓存下一边码值到数据库
                float[] nextSideValues = await _mySqlDataService.GetDistanceValuesBySideNumberAsync((sideNumber + 1).ToString());
                await _sqliteDataService.ReplaceSpindleDistanceValuesAsync((sideNumber + 1).ToString(), nextSideValues);

                //6. 下一边的断头进行处理
                sideNumber = sideNumber + 1; // 这里修改了 边号后面的边号都是传入的边号值+1
                await _taskController.ProcessAllBreakPointsAsync(taskData, _plcService, _sqliteDataService, _configService);
                //7. 告知plc 前往权限切换点
                await _plcService.GoToSwitchPointAsync();
                //8. 告知PLC 切换点码值
                await _taskController.WriteSwitchPointValueAsync(sideNumber, 001, _plcService, _sqliteDataService);
                // 9. 查找等待点
                string waitPointId = await _mySqlDataService.GetWaitPointIdByMachineIdAsync();
                //10. 控制agv 前往等待点
                uint waitPointIdUint = uint.Parse(waitPointId);
                await _agvService.NavigateToPointAsync(waitPointIdUint);
                //11. 等待agv 到达等待点
                while (true)
                {
                    bool arrived = await _agvService.HasReachedTargetPointAsync();
                    if (arrived)
                    {
                        Console.WriteLine("[MainCoordinator] AGV已到达等待点");
                        break;
                    }
                    Console.WriteLine("[MainCoordinator] 等待AGV到达等待点...");
                    await Task.Delay(2000);
                    }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainCoordinator] 处理断头任务出错: {ex.Message}");
                throw;
            }
        }
    }

    
}
