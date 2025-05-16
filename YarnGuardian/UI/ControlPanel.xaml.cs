using System;
using System.Windows;
using System.Windows.Controls;
using YarnGuardian.Common;

namespace YarnGuardian.UI
{
    /// <summary>
    /// ControlPanel.xaml 的交互逻辑
    /// </summary>
    public partial class ControlPanel : UserControl
    {
        // 定义事件名称常量
        public const string EVENT_START = "YarnGuardian.Start";
        public const string EVENT_STOP = "YarnGuardian.Stop";

        public ControlPanel()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 启动按钮点击事件处理
        /// </summary>
        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            // 使用事件总线发布启动事件
            EventHub.Instance.Publish(EVENT_START, DateTime.Now);
        }

        /// <summary>
        /// 停止按钮点击事件处理
        /// </summary>
        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            // 使用事件总线发布停止事件
            EventHub.Instance.Publish(EVENT_STOP, DateTime.Now);
        }
    }
} 