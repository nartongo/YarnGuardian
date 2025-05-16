using YarnGuardian.Services;

namespace YarnGuardian.Coordinator
{
    public class TaskController
    {
        private readonly MySqlDataService _mySqlDataService;
        // 其它依赖...

        public TaskController(MySqlDataService mySqlDataService /*, ... */)
        {
            _mySqlDataService = mySqlDataService;
            // ...
        }

        public async Task HandleStartRepairTask(dynamic eventData)
        {
            var sideNumber = eventData.SideNumber;
            var taskId = eventData.TaskId;
            // 业务逻辑...
            string switchPointId = await _mySqlDataService.GetSwitchPointIdBySideNumberAsync(sideNumber.ToString());
            // 其它业务处理
        }

        // 其它业务方法...
    }
}