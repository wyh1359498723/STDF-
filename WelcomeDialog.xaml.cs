using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace StdfAnalyzer;

public partial class WelcomeDialog : Window
{
    public WelcomeDialog()
    {
        InitializeComponent();
        BodyText.Text =
            "• 将 STDF / STD 文件加入解析队列，可拖动调整顺序后一键解析\r\n\r\n" +
            "• 多文件按顺序合并，相同（晶圆，X，Y）坐标保留最后一次测试结果\r\n\r\n" +
            "• 概览：Hard Bin / Soft Bin 分布、良率、测试项汇总\r\n\r\n" +
            "• 测试数据表与导出 CSV\r\n\r\n" +
            "• 晶圆图：缩放与拖动浏览，可切换显示 HBin / SBin 数值\r\n\r\n" +
            "• 导出晶圆 TXT 图档（Pass 为 1，Fail 为 X）\r\n\r\n" +
            "• 导出 TMB 图档（导出前选择 HBin 或 SBin，含 Bin 汇总）\r\n\r\n" +
            "• 导出标准比对用 Excel（转标准文件）";
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Close();

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
