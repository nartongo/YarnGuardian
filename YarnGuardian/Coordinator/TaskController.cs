using YarnGuardian.Services;
using System.Text.Json;
using System.Linq;
using YarnGuardian.Common;

namespace YarnGuardian.Coordinator
{
    public class TaskController
    {
        private readonly MySqlDataService _mySqlDataService;
        private readonly SQLiteDataService _sqliteDataService;
        // 其它依赖...

        public TaskController(MySqlDataService mySqlDataService, SQLiteDataService sqliteDataService /*, ... */)
        {
            _mySqlDataService = mySqlDataService;
            _sqliteDataService = sqliteDataService;
            // ...
        }

        public async Task<string> HandleStartRepairTask(dynamic eventData)
        {
            var sideNumber = eventData.SideNumber;
            var taskId = eventData.TaskId;
            // 业务逻辑...
            string switchPointId = await _mySqlDataService.GetSwitchPointIdBySideNumberAsync(sideNumber.ToString());
            // 其它业务处理
            return switchPointId;
        }

        // 获取此面断头数据
        public async Task<int[]> GetBreakPointData(dynamic eventData)
        {
            var sideNumber = eventData.SideNumber;
            var taskId = eventData.TaskId;
            // 1. 查询 MySQL 断头数据
            int[] breakPointData = await _mySqlDataService.GetNonZeroValuesBySideNumberAsync(sideNumber);
            // 2. 缓存到 SQLite
            await _sqliteDataService.ReplaceSideValuesAsync((int)sideNumber, breakPointData);
            // 3. 排序逻辑
            string sortOrder = null;
            if (sideNumber % 2 == 1)
                //奇数边号读取配置文件中的排序方式
                sortOrder = ConfigService.Instance.GetBreakPointSortOddSide();
            else
                //偶数边号读取配置文件中的排序方式
                sortOrder = ConfigService.Instance.GetBreakPointSortEvenSide();
            if (sortOrder == "Asc")
                breakPointData = breakPointData.OrderBy(x => x).ToArray();
            else if (sortOrder == "Desc")
                breakPointData = breakPointData.OrderByDescending(x => x).ToArray();
            // 4. 返回排序后的数据
            return breakPointData;
        }

        /// <summary>
        /// 前往切换点的完整流程
        /// </summary>
        /// <param name="eventData">包含 SideNumber、TaskId 等</param>
        public async Task ExecuteRepairTaskAsync(dynamic eventData, AGVService agvService, PLCService plcService)
        {
            var sideNumber = eventData.SideNumber;
            // 1. 查询 switch_point_id
            string switchPointId = await HandleStartRepairTask(eventData);
            // 2. 控制 AGV 移动到指定位置
            bool sendSuccess = await agvService.NavigateToPointAsync(uint.Parse(switchPointId));
            if (!sendSuccess)
            {
                Console.WriteLine("[TaskController] AGV 移动到指定位置失败。");
                return;
            }
            else
            {
                Console.WriteLine("[TaskController] AGV 移动到指定位置成功。");
            }
            // 3. 轮询判断 AGV 是否到达
            while (true)
            {
                bool isReached = await agvService.HasReachedTargetPointAsync();
                if (isReached)
                {
                    Console.WriteLine("AGV 已到达目标点！");
                    break;
                }
                else
                {
                    Console.WriteLine("AGV 尚未到达，等待 2 秒后重试...");
                    await Task.Delay(2000);
                }
            }
            // 4. 告知 PLC 到达权限切换点
            await plcService.WriteCoilAsync(PLCService.PLCAddresses.SWITCH_POINT_ARRIVED, true);
            // 5. 等待 PLC 完成权限切换反馈
            while (true)
            {
                bool isReceived = await plcService.ReadCoilAsync(PLCService.PLCAddresses.SWITCH_POINT_ARRIVED_FEEDBACK);
                if (isReceived)
                {
                    Console.WriteLine("PLC 收到切换点信号反馈！");
                    break;
                }
                else
                {
                    Console.WriteLine("PLC 未收到切换点信号反馈，等待 2 秒后重试...");
                    await Task.Delay(2000);
                }
            }
        }



        /// <summary>
        /// 执行所有断头处理完整流程
        /// </summary>
        /// <param name="eventData">包含 SideNumber、TaskId 等</param>
        /// <param name="plcService">PLC服务</param>
        /// <param name="sqliteDataService">SQLite服务</param>
        /// <param name="configService">配置服务</param>
        public async Task ProcessAllBreakPointsAsync(YarnGuardian.Common.RepairTaskMsg taskData, PLCService plcService, SQLiteDataService sqliteDataService, ConfigService configService)
        {
            int sideNumber = taskData.SideNumber;
            
            // 获取所有断头数据
            int[] breakPoints = await GetBreakPointData(taskData);
            if (breakPoints == null || breakPoints.Length == 0)
            {
                Console.WriteLine($"[TaskController] 边号{sideNumber}未获取到断头锭号，流程中止。");
                return;
            }
            
            Console.WriteLine($"[TaskController] 边号{sideNumber}获取到{breakPoints.Length}个断头锭号，开始处理。");
            
            // 转换为List以便移除元素
            List<int> remainingBreakPoints = new(breakPoints);
            
            // 循环处理每个断头，处理完就从列表中移除
            while (remainingBreakPoints.Count > 0)
            {
                int spindle = remainingBreakPoints[0];
                Console.WriteLine($"[TaskController] 处理断头锭号: {spindle}");
                
                // 处理单个断头
                await ProcessSingleBreakPointAsync(sideNumber, spindle, plcService, sqliteDataService, configService);
                
                // 从列表中移除已处理的断头
                remainingBreakPoints.RemoveAt(0);
            }
            
            Console.WriteLine($"[TaskController] 边号{sideNumber}的所有断头处理完成。");
        }


        /// <summary>
        /// 执行单个断头处理完整流程（锭号查码值、写PLC、移动、皮辊信号、等待完成）
        /// </summary>
        /// <param name="eventData">包含 SideNumber、TaskId 等</param>
        /// <param name="plcService">PLC服务</param>
        /// <param name="sqliteDataService">SQLite服务</param>
        /// <param name="configService">配置服务</param>
        private async Task ProcessSingleBreakPointAsync(int sideNumber, int spindle, PLCService plcService, SQLiteDataService sqliteDataService, ConfigService configService)
        {
            

            // 1. 查找该锭号对应的码值（distance_value）
            float? distanceValue = await sqliteDataService.GetDistanceValueBySpindleAsync(sideNumber.ToString(), spindle);
            if (distanceValue == null)
            {
                Console.WriteLine($"[TaskController] 未找到边号{sideNumber}锭号{spindle}的码值，流程中止。");
                return;
            }
            Console.WriteLine($"[TaskController] 查到码值: {distanceValue}");

            // 2. 写入PLC D500
            await plcService.WriteRegisterFloatAsync(PLCService.PLCAddresses.SPINDLE_POSITION, distanceValue.Value);
            Console.WriteLine($"[TaskController] 已写入PLC D500: {distanceValue}");

            // 3. 执行移动信号 M510
            await plcService.WriteCoilAsync(PLCService.PLCAddresses.CONFIGURE_MOVE, true);
            Console.WriteLine("[TaskController] 已写入PLC M510: true（执行移动）");

            // 4. 轮询等待M600变为true（到达锭位）
            while (true)
            {
                bool arrived = await plcService.ReadCoilAsync(PLCService.PLCAddresses.SPINDLE_ARRIVAL);
                if (arrived)
                {
                    Console.WriteLine("[TaskController] PLC M600=TRUE，AGV已到达锭位。");
                    break;
                }
                else
                {
                    Console.WriteLine("[TaskController] 等待PLC M600=TRUE，2秒后重试...");
                    await Task.Delay(2000);
                }
            }

            // 5. 控制左右皮辊信号 M501（奇偶性由配置决定）
            bool rollerSignal = (sideNumber % 2 == 1)
                ? configService.GetRollerSignalForOddSide()
                : configService.GetRollerSignalForEvenSide();
            await plcService.WriteCoilAsync(PLCService.PLCAddresses.TRIGGER_ROLLERS, rollerSignal);
            Console.WriteLine($"[TaskController] 已写入PLC M501: {rollerSignal}（皮辊信号，边号{sideNumber}）");

            // 6. 轮询等待M601变为true（接头完成）
            while (true)
            {
                bool done = await plcService.ReadCoilAsync(PLCService.PLCAddresses.REPAIR_DONE);
                if (done)
                {
                    Console.WriteLine("[TaskController] PLC M601=TRUE，接头完成。");
                    break;
                }
                else
                {
                    Console.WriteLine("[TaskController] 等待PLC M601=TRUE，2秒后重试...");
                    await Task.Delay(2000);
                }
            }

            Console.WriteLine("[TaskController] 单个断头处理完整流程结束。");
        }

        /// <summary>
        /// 写入切换点码值到PLC D500，并等待AGV到位（M600）
        /// </summary>
        /// <param name="sideNumber">边号</param>
        /// <param name="spindle">锭号</param>
        /// <param name="plcService">PLC服务</param>
        /// <param name="sqliteDataService">SQLite服务</param>
        public async Task WriteSwitchPointValueAsync(int sideNumber, int spindle, PLCService plcService, SQLiteDataService sqliteDataService)
        {
            // 1. 查找该锭号对应的码值（distance_value）
            float? distanceValue = await sqliteDataService.GetDistanceValueBySpindleAsync(sideNumber.ToString(), spindle);
            if (distanceValue == null)
            {
                Console.WriteLine($"[TaskController] 未找到边号{sideNumber}锭号{spindle}的码值，流程中止。");
                return;
            }
            Console.WriteLine($"[TaskController] 查到切换点码值: {distanceValue}");

            // 2. 写入PLC D500
            await plcService.WriteRegisterFloatAsync(PLCService.PLCAddresses.SPINDLE_POSITION, distanceValue.Value);
            Console.WriteLine($"[TaskController] 已写入PLC D500: {distanceValue}");

            // 3. 执行移动信号 M510
            await plcService.WriteCoilAsync(PLCService.PLCAddresses.CONFIGURE_MOVE, true);
            Console.WriteLine("[TaskController] 已写入PLC M510: true（执行移动）");

            // 4. 轮询等待M600变为true（到达锭位）
            while (true)
            {
                bool arrived = await plcService.ReadCoilAsync(PLCService.PLCAddresses.SPINDLE_ARRIVAL);
                if (arrived)
                {
                    Console.WriteLine("[TaskController] PLC M600=TRUE，AGV已到达切换点锭位。");
                    break;
                }
                else
                {
                    Console.WriteLine("[TaskController] 等待PLC M600=TRUE（切换点），2秒后重试...");
                    await Task.Delay(2000);
                }
            }
        }
    }
}