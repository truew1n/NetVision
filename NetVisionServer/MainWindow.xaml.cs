using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NetVisionLibrary;
using Newtonsoft.Json;

namespace NetVisionServer
{
    public partial class MainWindow : Window
    {
        private readonly TcpConnectionManager _connectionManager;
        private readonly UIStateManager _uiManager;
        private ObservableCollection<Record> _records;
        private ObservableCollection<string> _columnNames;
        private TimeSpan _currentTimerValue;
        private readonly DispatcherTimer _serverTimer; // Timer for server-side countdown

        public MainWindow()
        {
            InitializeComponent();
            _records = new ObservableCollection<Record>();
            _columnNames = new ObservableCollection<string>();
            _currentTimerValue = TimeSpan.Zero;

            _serverTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _serverTimer.Tick += OnServerTimerTick;

            _connectionManager = new TcpConnectionManager(isServer: true);
            _connectionManager.OnMessageReceived += async (message, clientId) => await HandleClientMessage(message, clientId);
            _connectionManager.OnConnectionStateChanged += (isConnected, message, clientCount) =>
            {
                Dispatcher.Invoke(() =>
                {
                    _uiManager.UpdateConnectionState(isConnected, message, clientCount);
                    if (!isConnected)
                    {
                        _serverTimer.Stop();
                        _currentTimerValue = TimeSpan.Zero;
                        _uiManager.UpdateTimerLabel("00:00:00");
                    }
                });
            };

            _uiManager = new UIStateManager(
                statusLabel: statusLabel,
                statusEllipse: statusEllipse,
                connectionTimerText: connectionTimerText,
                connectionTimer: new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) },
                timerLabel: timerLabel,
                clientCountLabel: clientCountLabel,
                receivedDataGrid: receivedDataGrid);

            InitializeDataGrid();

            StartServerAsync();
        }

        private async void StartServerAsync()
        {
            try
            {
                await _connectionManager.StartListeningAsync(25565);
            }
            catch (Exception ex)
            {
                _uiManager.UpdateConnectionState(false, $"Error starting server: {ex.Message}", _connectionManager.ClientCount);
            }
        }

        private void InitializeDataGrid()
        {
            receivedDataGrid.AutoGenerateColumns = false;
            receivedDataGrid.ItemsSource = _records;
        }

        private async Task HandleClientMessage(string message, string? clientId)
        {
            if (message.StartsWith("DATA:"))
            {
                await HandleDataMessage(message, clientId);
            }
            else if (message.StartsWith("TIMER:"))
            {
                await HandleTimerMessage(message, clientId);
            }
            else if (message == "RESET")
            {
                await HandleResetMessage();
            }
            else if (message == "STOP")
            {
                await HandleStopMessage();
            }
        }

        private async Task HandleDataMessage(string message, string? clientId)
        {
            try
            {
                string json = message.Substring(5);
                var payload = JsonConvert.DeserializeObject<Payload>(json);
                if (payload == null || payload.Columns == null || payload.Record == null)
                {
                    await _connectionManager.SendMessageToClientAsync("CONFIRM", clientId);
                    return;
                }

                var record = payload.Record;
                record.ColumnOrder = payload.Columns.ToList();

                Dispatcher.Invoke(() =>
                {
                    // Update columns if new ones are present
                    foreach (var column in payload.Columns)
                    {
                        if (!_columnNames.Contains(column))
                        {
                            _columnNames.Add(column);
                            receivedDataGrid.Columns.Add(new DataGridTextColumn
                            {
                                Header = column,
                                Binding = new System.Windows.Data.Binding($"[{column}]")
                            });
                        }
                    }

                    // Ensure all columns in ColumnOrder exist in Properties
                    foreach (var column in record.ColumnOrder)
                    {
                        if (!record.Properties.ContainsKey(column))
                        {
                            record[column] = string.Empty; // Triggers PropertyChanged
                        }
                    }

                    _records.Add(record);
                });

                await _connectionManager.SendMessageToClientAsync("CONFIRM", clientId);
            }
            catch (Exception ex)
            {
                await _connectionManager.SendMessageToClientAsync("CONFIRM", clientId);
                Dispatcher.Invoke(() =>
                {
                    _uiManager.UpdateConnectionState(true, $"Error processing data: {ex.Message}", _connectionManager.ClientCount);
                });
            }
        }

        private async Task HandleTimerMessage(string message, string? clientId)
        {
            try
            {
                string time = message.Substring(6);
                if (TimeSpan.TryParse(time, out TimeSpan newTimerValue) && newTimerValue >= TimeSpan.Zero)
                {
                    _currentTimerValue = newTimerValue;
                    Dispatcher.Invoke(() =>
                    {
                        _uiManager.UpdateTimerLabel(newTimerValue.ToString(@"hh\:mm\:ss"));
                        if (newTimerValue > TimeSpan.Zero && !_serverTimer.IsEnabled)
                        {
                            _serverTimer.Start(); // Start server timer if not already running
                        }
                    });
                    await _connectionManager.SendMessageToOthersAsync(message, clientId);
                }
            }
            catch (Exception)
            {
                // Ignore parsing errors
            }
        }

        private async Task HandleStopMessage()
        {
            _serverTimer.Stop();
            Dispatcher.Invoke(() => _uiManager.UpdateTimerLabel(_currentTimerValue.ToString(@"hh\:mm\:ss")));
            await _connectionManager.SendMessageAsync("STOP");
        }

        private async Task HandleResetMessage()
        {
            _serverTimer.Stop();
            _currentTimerValue = TimeSpan.Zero;
            Dispatcher.Invoke(() => _uiManager.UpdateTimerLabel("00:00:00"));
            await _connectionManager.SendMessageAsync("RESET");
        }

        private async void OnServerTimerTick(object? sender, EventArgs e)
        {
            if (_currentTimerValue > TimeSpan.Zero)
            {
                _currentTimerValue = _currentTimerValue.Subtract(TimeSpan.FromSeconds(1));
                string timeString = _currentTimerValue.ToString(@"hh\:mm\:ss");
                Dispatcher.Invoke(() => _uiManager.UpdateTimerLabel(timeString));
                // Broadcast updated timer value to all connected clients.
                await _connectionManager.SendMessageAsync($"TIMER:{timeString}");
            }
            else
            {
                _serverTimer.Stop();
                Dispatcher.Invoke(() => _uiManager.UpdateTimerLabel("00:00:00"));
                // Broadcast final timer value
                await _connectionManager.SendMessageAsync("TIMER:00:00:00");
            }
        }

        // Strongly-typed payload class for deserialization
        private class Payload
        {
            public string[]? Columns { get; set; }
            public Record? Record { get; set; }
        }
    }
}