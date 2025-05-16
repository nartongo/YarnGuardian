namespace YarnGuardian.Common
{
    public class EventHub
    {
        // 全局唯一实例，保证所有模块使用相同的事件总线
        private static readonly EventHub _instance = new EventHub();
        
        // 存储事件名称和对应的处理器列表
        private readonly Dictionary<string, List<Action<object>>> _eventHandlers = new Dictionary<string, List<Action<object>>>();

        /// <summary>
        /// 私有构造函数确保单例模式
        /// 防止外部直接创建实例
        /// </summary>
        private EventHub() { }

        /// <summary>
        /// 获取EventHub的全局单例实例
        /// </summary>
        public static EventHub Instance => _instance;

        /// <summary>
        /// 订阅事件
        /// 注册一个事件处理器，当指定事件发生时会被调用
        /// </summary>
        /// <param name="eventName">要订阅的事件名称</param>
        /// <param name="handler">事件处理器委托，当事件发布时会被调用</param>
        public void Subscribe(string eventName, Action<object> handler)
        {
            // 如果事件名称不存在于字典中，创建一个新的处理器列表
            if (!_eventHandlers.ContainsKey(eventName))
            {
                _eventHandlers[eventName] = new List<Action<object>>();
            }
            
            // 添加处理器到对应事件的列表中
            _eventHandlers[eventName].Add(handler);
        }

        /// <summary>
        /// 取消订阅事件
        /// 移除指定的事件处理器，使其不再接收事件通知
        /// </summary>
        /// <param name="eventName">要取消订阅的事件名称</param>
        /// <param name="handler">要移除的事件处理器</param>
        public void Unsubscribe(string eventName, Action<object> handler)
        {
            // 如果事件存在于字典中，从处理器列表中移除指定处理器
            if (_eventHandlers.ContainsKey(eventName))
            {
                _eventHandlers[eventName].Remove(handler);
            }
        }

        /// <summary>
        /// 发布事件
        /// 通知所有订阅了指定事件的处理器，并传递相关数据
        /// </summary>
        /// <param name="eventName">要发布的事件名称</param>
        /// <param name="data">要传递给事件处理器的数据</param>
        public void Publish(string eventName, object data)
        {
            // 如果事件存在于字典中，调用所有注册的处理器
            if (_eventHandlers.ContainsKey(eventName))
            {
                // 遍历所有处理器并调用
                foreach (var handler in _eventHandlers[eventName])
                {
                    try 
                    {
                        // 调用处理器，传递事件数据
                        handler.Invoke(data);
                    }
                    catch (Exception ex)
                    {
                        // 捕获并记录处理器执行过程中的异常，防止一个处理器异常影响其他处理器
                        Console.WriteLine($"事件处理器执行出错: {ex.Message}, 事件: {eventName}");
                    }
                }
            }
        }
    }
}

