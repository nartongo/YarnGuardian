using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using YarnGuardian.UI;

namespace YarnGuardian;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly EventHandlers _eventHandlers;

    public MainWindow()
    {
        InitializeComponent();
        
        // 创建并注册事件处理器
        _eventHandlers = new EventHandlers();
        _eventHandlers.RegisterHandlers();
        
        // 窗口关闭时取消注册事件处理器
        Closed += MainWindow_Closed;
    }

    private void MainWindow_Closed(object sender, System.EventArgs e)
    {
        // 窗口关闭时取消注册事件处理器，避免内存泄漏
        _eventHandlers.UnregisterHandlers();
    }
}