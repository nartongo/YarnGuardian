
using YarnGuardian.Common;

namespace YarnGuardian.UI
{
    /// <summary>
    /// 演示如何订阅和处理ControlPanel发出的事件
    /// </summary>
    public class EventHandlers
    {
        /// <summary>
        /// 注册事件处理器
        /// </summary>
        public void RegisterHandlers()
        {
            // 订阅启动事件
            EventHub.Instance.Subscribe(ControlPanel.EVENT_START, OnStart);
            
            // 订阅停止事件
            EventHub.Instance.Subscribe(ControlPanel.EVENT_STOP, OnStop);
        }

        /// <summary>
        /// 取消注册事件处理器
        /// </summary>
        public void UnregisterHandlers()
        {
            // 取消订阅启动事件
            EventHub.Instance.Unsubscribe(ControlPanel.EVENT_START, OnStart);
            
            // 取消订阅停止事件
            EventHub.Instance.Unsubscribe(ControlPanel.EVENT_STOP, OnStop);
        }

        /// <summary>
        /// 处理启动事件
        /// </summary>
        private void OnStart(object data)
        {
            if (data is DateTime startTime)
            {
                Console.WriteLine($"系统启动于: {startTime}");
                // 在这里执行启动相关的逻辑
            }
        }

        /// <summary>
        /// 处理停止事件
        /// </summary>
        private void OnStop(object data)
        {
            if (data is DateTime stopTime)
            {
                Console.WriteLine($"系统停止于: {stopTime}");
                // 在这里执行停止相关的逻辑
            }
        }
    }
} 