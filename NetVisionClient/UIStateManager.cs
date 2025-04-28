using System;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace NetVisionClient
{
    public class UIStateManager
    {
        private readonly TextBlock _statusTextBlock;
        private readonly Ellipse _statusEllipse;
        private readonly TextBlock _connectionTimerText;
        private readonly DispatcherTimer _connectionTimer;
        private readonly TextBlock _confirmationText;
        private readonly Label _timerLabel;
        private readonly Button _connectButton;
        private readonly Button _disconnectButton;
        private readonly Button _sendButton;
        private readonly Button _setButton;
        private readonly Button _startButton;
        private readonly Button _stopButton;
        private readonly Button _resumeButton;
        private readonly Button _resetButton;
        private TimeSpan _connectionDuration;

        public UIStateManager(
            TextBlock statusTextBlock,
            Ellipse statusEllipse,
            TextBlock connectionTimerText,
            DispatcherTimer connectionTimer,
            TextBlock confirmationText,
            Label timerLabel,
            Button connectButton,
            Button disconnectButton,
            Button sendButton,
            Button setButton,
            Button startButton,
            Button stopButton,
            Button resumeButton,
            Button resetButton)
        {
            _statusTextBlock = statusTextBlock;
            _statusEllipse = statusEllipse;
            _connectionTimerText = connectionTimerText;
            _connectionTimer = connectionTimer;
            _confirmationText = confirmationText;
            _timerLabel = timerLabel;
            _connectButton = connectButton;
            _disconnectButton = disconnectButton;
            _sendButton = sendButton;
            _setButton = setButton;
            _startButton = startButton;
            _stopButton = stopButton;
            _resumeButton = resumeButton;
            _resetButton = resetButton;
            _connectionDuration = TimeSpan.Zero;

            _connectionTimer.Tick += OnConnectionTimerTick;
            InitializeUi();
        }

        private void InitializeUi()
        {
            _statusTextBlock.Text = "Disconnected";
            _statusEllipse.Fill = Brushes.Red;
            _connectionTimerText.Text = "00:00:00";
            _confirmationText.Text = "None";
            _timerLabel.Content = "00:00:00";
            _connectButton.IsEnabled = true;
            _disconnectButton.IsEnabled = false;
            _sendButton.IsEnabled = false;
            _setButton.IsEnabled = true;
            _startButton.IsEnabled = false;
            _stopButton.IsEnabled = false;
            _resumeButton.IsEnabled = false;
            _resetButton.IsEnabled = false;
        }

        public void UpdateConnectionState(bool isConnected, string message, int clientCount)
        {
            _statusTextBlock.Text = isConnected ? "Connected" : "Disconnected";
            _statusEllipse.Fill = isConnected ? Brushes.Green : Brushes.Red;
            _confirmationText.Text = message;

            _connectButton.IsEnabled = !isConnected;
            _disconnectButton.IsEnabled = isConnected;
            _sendButton.IsEnabled = isConnected;

            if (isConnected)
            {
                // Start connection timer on connect
                _connectionDuration = TimeSpan.Zero;
                _connectionTimerText.Text = "00:00:00";
                _connectionTimer.Start();
            }
            else
            {
                // Stop and reset connection timer on disconnect
                _connectionTimer.Stop();
                _connectionDuration = TimeSpan.Zero;
                _connectionTimerText.Text = "00:00:00";

                // Update timer button states based on timer value
                string timerValue = _timerLabel.Content.ToString();
                bool hasTimerValue = timerValue != "00:00:00";
                _setButton.IsEnabled = true;
                _startButton.IsEnabled = false;
                _stopButton.IsEnabled = false;
                _resumeButton.IsEnabled = hasTimerValue;
                _resetButton.IsEnabled = hasTimerValue;
            }
        }

        public void UpdateConfirmationMessage(string message)
        {
            _confirmationText.Text = message;
        }

        public void UpdateTimerLabel(string time)
        {
            _timerLabel.Content = time;
        }

        public void UpdateTimerButtons(string state, TimeSpan timerValue)
        {
            bool hasTimerValue = timerValue > TimeSpan.Zero;

            switch (state)
            {
                case "Set":
                    // After Set: Set, Start, Reset enabled
                    _setButton.IsEnabled = true;
                    _startButton.IsEnabled = true;
                    _stopButton.IsEnabled = false;
                    _resumeButton.IsEnabled = false;
                    _resetButton.IsEnabled = true;
                    break;

                case "Ticking":
                    // Timer ticking: Stop, Reset enabled
                    _setButton.IsEnabled = false;
                    _startButton.IsEnabled = false;
                    _stopButton.IsEnabled = true;
                    _resumeButton.IsEnabled = false;
                    _resetButton.IsEnabled = true;
                    break;

                case "Stopped":
                    // After Stop or non-originating client: Resume, Set, Reset enabled if timerValue > 0
                    _setButton.IsEnabled = true;
                    _startButton.IsEnabled = false;
                    _stopButton.IsEnabled = false;
                    _resumeButton.IsEnabled = hasTimerValue;
                    _resetButton.IsEnabled = hasTimerValue;
                    break;

                default:
                    // Default: only Set enabled (e.g., initial state or after reset with timerValue = 0)
                    _setButton.IsEnabled = true;
                    _startButton.IsEnabled = false;
                    _stopButton.IsEnabled = false;
                    _resumeButton.IsEnabled = false;
                    _resetButton.IsEnabled = false;
                    break;
            }
        }

        private void OnConnectionTimerTick(object? sender, EventArgs e)
        {
            _connectionDuration = _connectionDuration.Add(TimeSpan.FromSeconds(1));
            _connectionTimerText.Text = _connectionDuration.ToString(@"hh\:mm\:ss");
        }
    }
}