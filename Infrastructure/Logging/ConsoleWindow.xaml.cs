using System.Windows;

namespace PT200Emulator.UI
{
    public partial class ConsoleWindow : Window
    {
        public ConsoleWindow()
        {
            InitializeComponent();
        }

        public void AppendLog(string message)
        {
            LogTextBox.AppendText(message + Environment.NewLine);
            LogTextBox.ScrollToEnd();
        }
    }
}