using NetMQ; // 引入 NetMQ 命名空间
using NetMQ.Sockets; // 引入 NetMQ 套接字类型
using Newtonsoft.Json; // 引入 Newtonsoft.Json 用于 JSON 序列化和反序列化
using YarnGuardian.Common; 

// ZeroMqBackendConfig 模型定义在此命名空间下
namespace YarnGuardian.Services
{
    /// <summary>
    /// ZeroMQ 客户端的配置。
    /// 包含用于发布和订阅的地址。
    /// </summary>
    public class ZeroMqBackendConfig
    {
        /// <summary>
        /// 此客户端将向其发布消息的地址。
        /// 后端服务器应在此地址上绑定一个 SUB 套接字。
        /// 示例: "tcp://localhost:5555"
        /// </summary>
        public string PublishAddress { get; set; }

        /// <summary>
        /// 此客户端将从中订阅来自后端消息的地址。
        /// 后端服务器应在此地址上绑定一个 PUB 套接字。
        /// 示例: "tcp://localhost:5556"
        /// </summary>
        public string SubscribeAddress { get; set; }
    }

    
}

namespace YarnGuardian.Services
{
    /// <summary>
    /// ZeroMQ 客户端
    /// 负责使用 ZeroMQ 套接字 (NetMQ) 建立通信。
    /// 使用发布-订阅模式实现异步消息通信。
    /// </summary>
    public class ZeroMqClient : IDisposable
    {
        private PublisherSocket _publisherSocket; // 用于向后端发送消息
        private SubscriberSocket _subscriberSocket; // 用于从后端接收消息
        private NetMQPoller _poller; // 异步处理从后端收到的信息

        private readonly ZeroMqBackendConfig _config; // ZeroMQ 配置信息
        
        //机器id从配置文件中读取 agvid 和 机器id 共用
        private readonly string _machineId; // 机器ID，用于消息主题
        private readonly EventHub _eventHub; 

        // 线程安全的字典，用于存储不同主题的处理程序
        private readonly Dictionary<string, Action<string>> _topicHandlers = new Dictionary<string, Action<string>>();
        private readonly object _handlerLock = new object(); // 用于同步对 _topicHandlers 的访问

        private CancellationTokenSource _cancellationTokenSource; // 用于取消轮询器操作
        private bool _isRunning = false; // 标记客户端是否正在运行

        /// <summary>
        /// ZeroMqClient 的构造函数。
        /// </summary>
        /// <param name="config">ZeroMQ 后端配置 (发布/订阅地址)。</param>
        /// <param name="machineId">机器 ID，用于消息主题以进行消息路由。</param>
        public ZeroMqClient(ZeroMqBackendConfig config, string machineId)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _machineId = machineId ?? throw new ArgumentNullException(nameof(machineId));
            _eventHub = EventHub.Instance; // 使用 YarnGuardian.Common.EventHub 的单例
        }

        /// <summary>
        /// 新增：直接从ConfigService读取配置的构造函数。
        /// </summary>
        public ZeroMqClient(ConfigService configService)
        {
            if (configService == null) throw new ArgumentNullException(nameof(configService));
            _config = new ZeroMqBackendConfig
            {
                PublishAddress = configService.GetZeroMqPublishAddress(),
                SubscribeAddress = configService.GetZeroMqSubscribeAddress()
            };
            _machineId = configService.GetMachineId();
            _eventHub = EventHub.Instance;
        }

        /// <summary>
        /// 连接并初始化 ZeroMQ 套接字。
        /// 设置一个发布者套接字以发送消息，以及一个订阅者套接字以接收消息。
        /// 启动一个轮询器以异步处理传入消息。
        /// </summary>
        /// <returns>如果设置成功则为 true，否则为 false。</returns>
        public bool Connect()
        {
            if (_isRunning)
            {
                Console.WriteLine("ZeroMQ 客户端已在运行。");
                return true;
            }

            try
            {
                _publisherSocket = new PublisherSocket();
                // 客户端将其 PUB 套接字连接到后端 SUB 套接字绑定的地址。
                _publisherSocket.Connect(_config.PublishAddress);
                Console.WriteLine($"ZeroMQ 发布者已连接到: {_config.PublishAddress}");

                _subscriberSocket = new SubscriberSocket();
                // 客户端将其 SUB 套接字连接到后端 PUB 套接字绑定的地址。
                _subscriberSocket.Connect(_config.SubscribeAddress);
                Console.WriteLine($"ZeroMQ 订阅者已连接到: {_config.SubscribeAddress}");

                // 初始化并启动订阅者套接字的轮询器
                _cancellationTokenSource = new CancellationTokenSource();
                _poller = new NetMQPoller { _subscriberSocket }; // 将订阅者套接字添加到轮询器

                // 当订阅者套接字上有消息准备好接收时，触发此事件
                _subscriberSocket.ReceiveReady += (s, e) => HandleReceivedMessage(e.Socket);
                
                // 在一个新线程中运行轮询器，以避免阻塞主线程
                Task.Factory.StartNew(() => _poller.Run(), _cancellationTokenSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

                _isRunning = true;
                Console.WriteLine("ZeroMQ 客户端启动成功。");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"连接/初始化 ZeroMQ 客户端失败: {ex.Message}");
                _isRunning = false;
                // 清理部分初始化的资源
                _publisherSocket?.Close();
                _publisherSocket?.Dispose();
                _subscriberSocket?.Close();
                _subscriberSocket?.Dispose();
                _poller?.Dispose();
                return false;
            }
        }

        /// <summary>
        /// 处理在订阅者套接字上接收到的消息。
        /// 期望接收一个多部分消息: [主题帧, 消息帧]。
        /// </summary>
        private void HandleReceivedMessage(NetMQSocket socket)
        {
            try
            {
                // 消息期望格式为 [主题, 内容]
                string topic = socket.ReceiveFrameString(out bool hasMore); // 接收主题帧
                if (hasMore) // 检查是否还有更多帧（即消息内容）
                {
                    string messageJson = socket.ReceiveFrameString(); // 接收消息内容帧
                    Console.WriteLine($"ZeroMQ: 在主题 '{topic}' 上收到消息");

                    Action<string> handler;
                    lock (_handlerLock) // 锁定以确保线程安全地访问 _topicHandlers
                    {
                        _topicHandlers.TryGetValue(topic, out handler); // 根据主题查找处理程序
                    }

                    if (handler != null)
                    {
                        try
                        {
                            handler(messageJson); // 调用相应的处理程序
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"处理 ZeroMQ 主题 '{topic}' 的消息出错: {ex.Message}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"没有为 ZeroMQ 主题注册处理程序: {topic}");
                    }
                }
                else
                {
                     Console.WriteLine($"ZeroMQ: 收到不完整的消息。主题: '{topic}', 但没有更多帧。");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ZeroMQ ReceiveReady 处理程序出错: {ex.Message}");
            }
        }


        /// <summary>
        /// 将消息发布到特定主题。
        /// 消息对象将被序列化为 JSON。
        /// 发送一个多部分消息: [主题帧, JSON消息帧]。
        /// </summary>
        /// <param name="topic">要将消息发布到的主题。</param>
        /// <param name="message">消息对象。</param>
        /// <returns>如果成功则为 true，否则为 false。</returns>
        public bool Publish(string topic, object message)
        {
            if (!_isRunning || _publisherSocket == null)
            {
                Console.WriteLine("ZeroMQ 客户端未连接或发布者套接字未初始化。无法发布消息。");
                return false;
            }

            try
            {
                string jsonMessage = JsonConvert.SerializeObject(message); // 将消息对象序列化为 JSON 字符串
                // 先发送主题作为第一帧，然后发送消息内容
                _publisherSocket.SendMoreFrame(topic) // 发送主题帧，并指示后面还有帧
                                .SendFrame(jsonMessage); // 发送消息内容帧
                Console.WriteLine($"ZeroMQ: 已发送消息到主题: {topic}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ZeroMQ: 发送消息到主题 {topic} 失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 订阅特定主题以接收消息。
        /// </summary>
        /// <param name="topic">要订阅的主题。</param>
        /// <param name="handler">处理此主题传入消息的回调函数。</param>
        public void Subscribe(string topic, Action<string> handler)
        {
            if (!_isRunning || _subscriberSocket == null)
            {
                Console.WriteLine("ZeroMQ 客户端未连接或订阅者套接字未初始化。无法订阅。");
                return;
            }

            try
            {
                lock (_handlerLock) // 锁定以确保线程安全地访问 _topicHandlers
                {
                    if (!_topicHandlers.ContainsKey(topic))
                    {
                         _topicHandlers.Add(topic, handler); // 添加新的主题处理程序
                    }
                    else // 允许重新订阅以更新处理程序，或记录警告
                    {
                        _topicHandlers[topic] = handler; 
                        Console.WriteLine($"ZeroMQ: 警告 - 正在为主题 '{topic}' 重新订阅或更新处理程序。");
                    }
                }
                _subscriberSocket.Subscribe(topic); // 告诉订阅者套接字开始接收指定主题的消息
                Console.WriteLine($"ZeroMQ: 已订阅主题: {topic}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ZeroMQ: 订阅主题 {topic} 失败: {ex.Message}");
            }
        }

       

        /// <summary>
        /// 向后端发送启动请求。
        /// 主题格式: "backend.start.start.{machineId}"
        /// </summary>
        public bool SendStartRequest()
        {
            var startRequest = new
            {
                type = "StartRequest", // 与原始类型一致
                machineId = _machineId,
                timestamp = DateTime.UtcNow.ToString("o") // ISO 8601 格式时间戳
            };
            // 主题结合了原始交换器和路由键的概念
            string topic = $"backend.start.start.{_machineId}";
            return Publish(topic, startRequest);
        }

        /// <summary>
        /// 向后端发送状态报告。
        /// 主题格式: "machine.{machineId}.status.status"
        /// </summary>
        public bool SendStatusReport(object status)
        {
            var statusReport = new
            {
                type = "StatusReport", // 与原始类型一致
                machineId = _machineId,
                timestamp = DateTime.UtcNow.ToString("o"),
                status = status // 状态对象
            };
            string topic = $"machine.{_machineId}.status.status";
            return Publish(topic, statusReport);
        }

        /// <summary>
        /// 向后端发送任务确认。
        /// 主题格式: "backend.ack.ack.{machineId}"
        /// </summary>
        public bool SendTaskAck(string taskId, string status)
        {
            var taskAck = new
            {
                type = "TaskAck", // 与原始类型一致
                machineId = _machineId,
                taskId = taskId,
                status = status,
                timestamp = DateTime.UtcNow.ToString("o")
            };
            // 用于此机器的通用任务 ACK 的主题
            string topic = $"backend.ack.ack.{_machineId}";
            return Publish(topic, taskAck);
        }

        

        /// <summary>
        /// 设置用于处理来自后端的 StartAck 消息的处理程序。
        /// 订阅主题: "backend.ack.ack.{machineId}" (发往此机器的所有 ack 使用相同主题，
        /// 消息类型字段将用于区分)
        /// </summary>
        public void SetupStartAckHandler()
        {
            // 后端将向此机器订阅的主题发布 StartAck 消息。
            // 发往此机器的 ACK 的示例主题:
            string topic = $"backend.ack.ack.{_machineId}";
            Subscribe(topic, messageJson =>
            {
                try
                {
                    dynamic ack = JsonConvert.DeserializeObject(messageJson); // 反序列化 JSON 消息
                    // 检查消息中的 'type' 字段以确保它是 StartAck
                    if (ack != null && ack.type == "StartAck")
                    {
                        Console.WriteLine($"ZeroMQ: 收到 StartAck: {messageJson}");
                        _eventHub.Publish("BackendStartAck", ack); // 转发到本地事件中心
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"处理 StartAck 消息出错: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 设置用于处理来自后端的 StatusAck 消息的处理程序。
        /// 订阅主题: "backend.ack.status.ack.{machineId}"
        /// </summary>
        public void SetupStatusAckHandler()
        {
             // 用于状态 ACK 的更具体主题
            string topic = $"backend.ack.status.ack.{_machineId}";
            Subscribe(topic, messageJson =>
            {
                try
                {
                    dynamic ack = JsonConvert.DeserializeObject(messageJson);
                    if (ack != null && ack.type == "StatusAck")
                    {
                        Console.WriteLine($"ZeroMQ: 收到 StatusAck: {messageJson}");
                        _eventHub.Publish("BackendStatusAck", ack);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"处理 StatusAck 消息出错: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 设置用于处理来自后端的 TaskAssignment 消息的处理程序。
        /// (jin 的逻辑: 接受任务，解析，发布到 EventHub，发送 ACK)
        /// 订阅主题: "machine.{machineId}.task.task"
        /// </summary>
        public void SetupTaskAssignmentHandler()
        {
            string topic = $"machine.{_machineId}.task.task"; // 发往此机器的任务
            Subscribe(topic, messageJson =>
            {
                try
                {
                    dynamic taskAssignment = JsonConvert.DeserializeObject(messageJson);
                    if (taskAssignment != null && taskAssignment.type == "TaskAssignment")
                    {
                        Console.WriteLine($"ZeroMQ: 收到 TaskAssignment: {messageJson}");

                        // jin: 创建 TaskController 期望的 Type 的事件数据
                        var eventData = new
                        {
                            Type = "StartRepairTask", // 根据原始逻辑
                            SideNumber = taskAssignment.spinningMachineId, // 边号
                            TaskId = taskAssignment.taskId, // 任务ID
                            OriginalMessage = taskAssignment // 如果下游需要，则包含原始消息
                        };

                        _eventHub.Publish("BackendCommandReceived", eventData); // 发布到事件中心

                        // 向后端发送任务确认
                        SendTaskAck(taskAssignment.taskId.ToString(), "Received");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"处理 TaskAssignment 消息出错: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 释放 ZeroMQ 资源 (套接字, 轮询器)。
        /// </summary>
        public void Dispose()
        {
            if (!_isRunning) return;

            Console.WriteLine("正在释放 ZeroMQClient 资源...");
            _cancellationTokenSource?.Cancel(); // 通知轮询器停止

            _poller?.Stop(); // NetMQPoller.Stop() 是阻塞的
            
            _publisherSocket?.Disconnect(_config.PublishAddress); // 断开连接
            _publisherSocket?.Close(); // 关闭套接字
            _publisherSocket?.Dispose(); // 释放套接字资源
            _publisherSocket = null;

            if (_subscriberSocket != null)
            {
                lock(_handlerLock)
                {
                    foreach(var topicKey in _topicHandlers.Keys.ToList()) // ToList() to avoid modification issues during iteration if Unsubscribe modifies the collection
                    {
                        try { _subscriberSocket.Unsubscribe(topicKey); } catch {} 
                    }
                    _topicHandlers.Clear();
                }
                _subscriberSocket.Disconnect(_config.SubscribeAddress);
                _subscriberSocket.Close();
                _subscriberSocket.Dispose();
                _subscriberSocket = null;
            }
            
            _poller?.Dispose(); 
            _poller = null;

            _cancellationTokenSource?.Dispose(); 
            _cancellationTokenSource = null;

            _isRunning = false;
            Console.WriteLine("ZeroMQClient 资源已释放。");
        }
    }
}
