using System.Windows;
using USBGuardian.App.UI;

namespace USBGuardian.App
{
    public partial class MainWindow
    {
        private void ReportProblem_Click(object sender, RoutedEventArgs e)
        {
            ReportIssueWindow dialog = new ReportIssueWindow { Owner = this };
            dialog.ShowDialog();
        }

        // Non-destructive UI test available only under Settings > Notification tests.
        // Does not create an alert, throw, send email, or modify any USB state.
        private void TestIssueReport_Click(object sender, RoutedEventArgs e)
        {
            ReportIssueWindow dialog = new ReportIssueWindow(
                new System.InvalidOperationException("Diagnostic form preview — not a real failure"),
                "UI preview") { Owner = this };
            dialog.ShowDialog();
        }
    }
}
