using YarnGuardian.Services;
using System.Text.Json;
using System.Linq;

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
    }
}