using System;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace NetVisionServer
{
    public class UIStateManager
    {
        private readonly TextBlock _statusLabel;
        private readonly Ellipse _statusEllipse;
        private readonly TextBlock _connectionTimerText;
        private readonly DispatcherTimer _connectionTimer;
        private readonly TextBlock _timerLabel;
        private readonly TextBlock _clientCountLabel;
        private readonly DataGrid _receivedDataGrid;
        private TimeSpan _connectionDuration;

        public UIStateManager(
            TextBlock statusLabel,
            Ellipse statusEllipse,
            TextBlock connectionTimerText,
            DispatcherTimer connectionTimer,
            TextBlock timerLabel,
            TextBlock clientCountLabel,
            DataGrid receivedDataGrid)
        {
            _statusLabel = statusLabel;
            _statusEllipse = statusEllipse;
            _connectionTimerText = connectionTimerText;
            _connectionTimer = connectionTimer;
            _timerLabel = timerLabel;
            _clientCountLabel = clientCountLabel;
            _receivedDataGrid = receivedDataGrid;
            _connectionDuration = TimeSpan.Zero;

            _connectionTimer.Tick += OnConnectionTimerTick;
            InitializeUi();
        }

        private void InitializeUi()
        {
            _statusLabel.Text = "Disconnected";
            _statusEllipse.Fill = Brushes.Red;
            _connectionTimerText.Text = "00:00:00";
            _timerLabel.Text = "00:00:00";
            _clientCountLabel.Text = "0";
        }

        public void UpdateConnectionState(bool isConnected, string message, int clientCount)
        {
            _statusLabel.Text = clientCount > 0 ? "Connected" : "Disconnected";
            _statusEllipse.Fill = clientCount > 0 ? Brushes.Green : Brushes.Red;
            _connectionTimerText.Text = clientCount > 0 ? _connectionDuration.ToString(@"hh\:mm\:ss") : "00:00:00";
            _clientCountLabel.Text = clientCount.ToString();

            if (clientCount > 0)
            {
                _connectionDuration = TimeSpan.Zero;
                _connectionTimer.Start();
            }
            else
            {
                _connectionTimer.Stop();
                _connectionDuration = TimeSpan.Zero;
            }
        }

        public void UpdateTimerLabel(string time)
        {
            _timerLabel.Text = time;
        }

        private void OnConnectionTimerTick(object? sender, EventArgs e)
        {
            _connectionDuration = _connectionDuration.Add(TimeSpan.FromSeconds(1));
            _connectionTimerText.Text = _connectionDuration.ToString(@"hh\:mm\:ss");
        }
    }
}