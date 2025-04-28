using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Newtonsoft.Json;
using Microsoft.Win32;
using NetVisionLibrary;

namespace NetVisionClient
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<Record> records;
        private TimeSpan timerValue;
        private DispatcherTimer dispatcherTimer;
        private ObservableCollection<string> columnNames;
        private TcpConnectionManager tcpManager;
        private UIStateManager uiManager;
        private bool isFormatting;
        private DateTime lastInputTime;
        private const int DebounceDelayMs = 50;
        private DataGridColumnHeader? selectedColumnHeader;
        private bool isWaitingForConfirm;

        public MainWindow()
        {
            InitializeComponent();
            columnNames = new ObservableCollection<string>();
            records = new ObservableCollection<Record>();
            isFormatting = false;
            lastInputTime = DateTime.MinValue;
            selectedColumnHeader = null;
            timerValue = TimeSpan.Zero;
            isWaitingForConfirm = false;

            if (File.Exists("data.json"))
            {
                string json = File.ReadAllText("data.json");
                var loadedRecords = JsonConvert.DeserializeObject<ObservableCollection<Record>>(json);
                if (loadedRecords != null)
                {
                    records = loadedRecords;
                    columnNames.Clear();
                    var firstRecord = records.FirstOrDefault();
                    if (firstRecord != null && firstRecord.ColumnOrder.Any())
                    {
                        foreach (var key in firstRecord.ColumnOrder)
                        {
                            if (!columnNames.Contains(key))
                            {
                                columnNames.Add(key);
                            }
                        }
                    }
                    else
                    {
                        foreach (var record in records)
                        {
                            foreach (var key in record.Properties.Keys)
                            {
                                if (!columnNames.Contains(key))
                                {
                                    columnNames.Add(key);
                                }
                            }
                        }
                    }
                }
            }

            DynamicDataGrid.AutoGenerateColumns = false;
            foreach (var colName in columnNames)
            {
                DynamicDataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = colName,
                    Binding = new System.Windows.Data.Binding($"[{colName}]")
                });
            }
            DynamicDataGrid.ItemsSource = records;

            dispatcherTimer = new DispatcherTimer();
            dispatcherTimer.Interval = TimeSpan.FromSeconds(1);
            dispatcherTimer.Tick += OnTimerTick;

            tcpManager = new TcpConnectionManager(isServer: false);
            tcpManager.OnMessageReceived += HandleMessageReceived;
            tcpManager.OnConnectionStateChanged += (isConnected, message, clientCount) =>
            {
                Dispatcher.Invoke(() =>
                {
                    uiManager.UpdateConnectionState(isConnected, message, clientCount);
                    if (!isConnected)
                    {
                        ResetTimer();
                        uiManager.UpdateConfirmationMessage("Disconnected from server.");
                    }
                });
            };

            uiManager = new UIStateManager(
                statusTextBlock: connectionStatus,
                statusEllipse: statusEllipse,
                connectionTimerText: connectionTimerText,
                connectionTimer: new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) },
                confirmationText: confirmationText,
                timerLabel: timerLabel,
                connectButton: connectButton,
                disconnectButton: disconnectButton,
                sendButton: sendButton,
                setButton: setButton,
                startButton: startButton,
                stopButton: stopButton,
                resumeButton: resumeButton,
                resetButton: resetButton
            );
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            foreach (var record in records)
            {
                record.ColumnOrder = columnNames.ToList();
            }

            string json = JsonConvert.SerializeObject(records);
            File.WriteAllText("data.json", json);
            tcpManager.DisconnectAsync().GetAwaiter().GetResult();
        }

        private void UpdateDataGridMessage(string message)
        {
            dataGridMessageText.Text = message;
        }

        private void OnColumnHeaderClick(object sender, RoutedEventArgs e)
        {
            if (sender is DataGridColumnHeader header)
            {
                if (selectedColumnHeader != null && selectedColumnHeader != header)
                {
                    selectedColumnHeader.Background = new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString("#2E2E2E"));
                }

                selectedColumnHeader = header;
                selectedColumnHeader.Background = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#4A4A4A"));

                UpdateDataGridMessage($"Column '{header.Column.Header?.ToString() ?? "Unknown"}' selected.");

                e.Handled = true;
            }
        }

        private void DeleteSelectedColumn()
        {
            if (selectedColumnHeader == null)
            {
                UpdateDataGridMessage("No column selected.");
                return;
            }

            if (columnNames.Count <= 1)
            {
                UpdateDataGridMessage("Cannot delete the last column.");
                return;
            }

            string? columnToDelete = selectedColumnHeader.Column.Header?.ToString();
            if (string.IsNullOrEmpty(columnToDelete))
            {
                UpdateDataGridMessage("Error: Invalid column selected.");
                return;
            }

            columnNames.Remove(columnToDelete);
            DynamicDataGrid.Columns.Remove(selectedColumnHeader.Column);

            foreach (var record in records)
            {
                record.Properties.Remove(columnToDelete);
                record.ColumnOrder = columnNames.ToList();
            }

            selectedColumnHeader.Background = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#2E2E2E"));
            selectedColumnHeader = null;
            UpdateDataGridMessage($"Column '{columnToDelete}' deleted.");
        }

        private void OnDeleteColumnClick(object sender, RoutedEventArgs e)
        {
            DeleteSelectedColumn();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                DeleteSelectedColumn();
                e.Handled = true;
            }
        }

        private void TimerTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if ((DateTime.Now - lastInputTime).TotalMilliseconds < DebounceDelayMs)
            {
                e.Handled = true;
                return;
            }

            if (!char.IsDigit(e.Text, 0))
            {
                e.Handled = true;
                return;
            }

            var textBox = sender as TextBox;
            if (textBox == null)
            {
                e.Handled = true;
                return;
            }

            if (isFormatting)
            {
                e.Handled = true;
                return;
            }

            string currentText = textBox.Text.Replace(":", "");
            string newText = currentText + e.Text;
            if (newText.Length > 6)
            {
                newText = newText.Substring(1);
            }
            else
            {
                newText = newText.PadLeft(6, '0');
            }

            Dispatcher.Invoke(() =>
            {
                isFormatting = true;
                string formattedText = $"{newText.Substring(0, 2)}:{newText.Substring(2, 2)}:{newText.Substring(4, 2)}";
                textBox.Text = formattedText;
                textBox.CaretIndex = formattedText.Length;
                isFormatting = false;
            });

            lastInputTime = DateTime.Now;
            e.Handled = true;
        }

        private void TimerTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Back)
            {
                return;
            }

            if ((DateTime.Now - lastInputTime).TotalMilliseconds < DebounceDelayMs)
            {
                e.Handled = true;
                return;
            }

            var textBox = sender as TextBox;
            if (textBox == null)
            {
                e.Handled = true;
                return;
            }

            if (isFormatting)
            {
                e.Handled = true;
                return;
            }

            string currentText = textBox.Text.Replace(":", "");
            if (currentText.Length > 0)
            {
                currentText = "0" + currentText.Substring(0, currentText.Length - 1);
            }

            Dispatcher.Invoke(() =>
            {
                isFormatting = true;
                string formattedText = $"{currentText.Substring(0, 2)}:{currentText.Substring(2, 2)}:{currentText.Substring(4, 2)}";
                textBox.Text = formattedText;
                textBox.CaretIndex = formattedText.Length;
                isFormatting = false;
            });

            lastInputTime = DateTime.Now;
            e.Handled = true;
        }

        private void TimerTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (isFormatting) return;

            var textBox = sender as TextBox;
            if (textBox == null) return;

            string text = textBox.Text.Replace(":", "");
            if (text.Length > 6)
            {
                text = text.Substring(text.Length - 6);
            }
            text = text.PadLeft(6, '0');

            Dispatcher.Invoke(() =>
            {
                isFormatting = true;
                string formattedText = $"{text.Substring(0, 2)}:{text.Substring(2, 2)}:{text.Substring(4, 2)}";
                textBox.Text = formattedText;
                textBox.CaretIndex = formattedText.Length;
                isFormatting = false;
            });
        }

        private void TimerTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;

            if (isFormatting) return;

            string text = textBox.Text.Replace(":", "");
            if (text.Length != 6) return;

            int hours = int.Parse(text.Substring(0, 2));
            int minutes = int.Parse(text.Substring(2, 2));
            int seconds = int.Parse(text.Substring(4, 2));

            long totalSeconds = hours * 3600 + minutes * 60 + seconds;
            hours = (int)(totalSeconds / 3600);
            totalSeconds %= 3600;
            minutes = (int)(totalSeconds / 60);
            seconds = (int)(totalSeconds % 60);

            if (hours > 99)
            {
                hours = 99;
                minutes = 59;
                seconds = 59;
            }

            Dispatcher.Invoke(() =>
            {
                isFormatting = true;
                string formattedText = $"{hours:D2}:{minutes:D2}:{seconds:D2}";
                textBox.Text = formattedText;
                isFormatting = false;
            });
        }

        private void OnAddRowClick(object sender, RoutedEventArgs e)
        {
            var newRecord = new Record();
            foreach (var colName in columnNames)
            {
                newRecord[colName] = "";
            }
            newRecord.ColumnOrder = columnNames.ToList();
            records.Add(newRecord);
            UpdateDataGridMessage("Row added.");
        }

        private void OnDeleteRowClick(object sender, RoutedEventArgs e)
        {
            if (DynamicDataGrid.SelectedItem != null)
            {
                try
                {
                    DynamicDataGrid.CommitEdit();
                    if (DynamicDataGrid.SelectedItem is Record selectedRecord)
                    {
                        records.Remove(selectedRecord);
                        UpdateDataGridMessage("Row deleted.");
                    }
                    else
                    {
                        UpdateDataGridMessage("Selected item is not a valid record.");
                    }
                }
                catch (Exception ex)
                {
                    UpdateDataGridMessage($"Error deleting row: {ex.Message}");
                }
            }
            else
            {
                UpdateDataGridMessage("No row selected.");
            }
        }

        private void OnAddColumnClick(object sender, RoutedEventArgs e)
        {
            string newColumnName;
            int columnNumber = columnNames.Count + 1;
            do
            {
                newColumnName = $"Column{columnNumber}";
                columnNumber++;
            } while (columnNames.Contains(newColumnName));

            columnNames.Add(newColumnName);
            DynamicDataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = newColumnName,
                Binding = new System.Windows.Data.Binding($"[{newColumnName}]")
            });

            foreach (var record in records)
            {
                record[newColumnName] = "";
                record.ColumnOrder = columnNames.ToList();
            }
            UpdateDataGridMessage($"Column '{newColumnName}' added.");
        }

        private void OnColumnHeaderDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGridColumnHeader header)
            {
                string? oldColumnName = header.Column.Header?.ToString();
                if (string.IsNullOrEmpty(oldColumnName))
                {
                    UpdateDataGridMessage("Error: Invalid column name.");
                    return;
                }

                var textBox = new TextBox
                {
                    Text = oldColumnName,
                    Width = header.ActualWidth,
                    Background = Brushes.Black,
                    Foreground = Brushes.White,
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(1)
                };

                header.Content = textBox;
                textBox.Focus();
                textBox.SelectAll();

                textBox.KeyDown += (s, ev) =>
                {
                    if (ev.Key == Key.Enter)
                    {
                        string newName = textBox.Text.Trim();
                        UpdateColumnName(header, oldColumnName, newName);
                    }
                    else if (ev.Key == Key.Escape)
                    {
                        header.Content = oldColumnName;
                        UpdateDataGridMessage("Column rename cancelled.");
                    }
                };
            }
        }

        private void UpdateColumnName(DataGridColumnHeader header, string oldColumnName, string newColumnName)
        {
            var otherColumnNames = columnNames.Where(name => name != oldColumnName).ToList();
            bool isDuplicate = otherColumnNames.Any(name => string.Equals(name, newColumnName, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(newColumnName) || isDuplicate)
            {
                UpdateDataGridMessage($"Invalid or duplicate column name. New name: '{newColumnName}' Existing names: {string.Join(", ", otherColumnNames)}");
                header.Content = oldColumnName;
                return;
            }

            int index = columnNames.IndexOf(oldColumnName);
            columnNames[index] = newColumnName;
            header.Content = newColumnName;
            header.Column.Header = newColumnName;

            foreach (var record in records)
            {
                if (record.Properties.ContainsKey(oldColumnName))
                {
                    string? value = record[oldColumnName];
                    record.Properties.Remove(oldColumnName);
                    record[newColumnName] = value ?? string.Empty;
                    record.ColumnOrder = columnNames.ToList();
                }
            }

            ((DataGridTextColumn)header.Column).Binding = new System.Windows.Data.Binding($"[{newColumnName}]");
            UpdateDataGridMessage($"Column renamed to '{newColumnName}'.");
        }

        private void OnMoveUpClick(object sender, RoutedEventArgs e)
        {
            if (DynamicDataGrid.SelectedItem != null && DynamicDataGrid.SelectedItem is Record selectedRecord)
            {
                int index = records.IndexOf(selectedRecord);
                if (index > 0)
                {
                    records.Move(index, index - 1);
                    UpdateDataGridMessage("Row moved up.");
                }
                else
                {
                    UpdateDataGridMessage("Row is already at the top.");
                }
            }
            else
            {
                UpdateDataGridMessage("No row selected.");
            }
        }

        private void OnMoveDownClick(object sender, RoutedEventArgs e)
        {
            if (DynamicDataGrid.SelectedItem != null && DynamicDataGrid.SelectedItem is Record selectedRecord)
            {
                int index = records.IndexOf(selectedRecord);
                if (index < records.Count - 1)
                {
                    records.Move(index, index + 1);
                    UpdateDataGridMessage("Row moved down.");
                }
                else
                {
                    UpdateDataGridMessage("Row is already at the bottom.");
                }
            }
            else
            {
                UpdateDataGridMessage("No row selected.");
            }
        }

        private void OnLoadDataClick(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                Title = "Load Data File",
                DefaultExt = "json"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    string filePath = openFileDialog.FileName;
                    string json = File.ReadAllText(filePath);
                    var loadedRecords = JsonConvert.DeserializeObject<ObservableCollection<Record>>(json) ?? new ObservableCollection<Record>();
                    records.Clear();
                    foreach (var record in loadedRecords)
                    {
                        records.Add(record);
                    }

                    columnNames.Clear();
                    var firstRecord = records.FirstOrDefault();
                    if (firstRecord != null && firstRecord.ColumnOrder.Any())
                    {
                        foreach (var key in firstRecord.ColumnOrder)
                        {
                            if (!columnNames.Contains(key))
                            {
                                columnNames.Add(key);
                            }
                        }
                    }
                    else
                    {
                        foreach (var record in records)
                        {
                            foreach (var key in record.Properties.Keys)
                            {
                                if (!columnNames.Contains(key))
                                {
                                    columnNames.Add(key);
                                }
                            }
                        }
                    }

                    DynamicDataGrid.Columns.Clear();
                    foreach (var colName in columnNames)
                    {
                        DynamicDataGrid.Columns.Add(new DataGridTextColumn
                        {
                            Header = colName,
                            Binding = new System.Windows.Data.Binding($"[{colName}]")
                        });
                    }
                    UpdateDataGridMessage("Data loaded successfully.");
                }
                catch (Exception ex)
                {
                    UpdateDataGridMessage($"Error loading data: {ex.Message}");
                }
            }
        }

        private void OnSaveDataClick(object sender, RoutedEventArgs e)
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                Title = "Save Data File",
                DefaultExt = "json",
                FileName = "data.json"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    foreach (var record in records)
                    {
                        record.ColumnOrder = columnNames.ToList();
                    }

                    string filePath = saveFileDialog.FileName;
                    string json = JsonConvert.SerializeObject(records);
                    File.WriteAllText(filePath, json);
                    UpdateDataGridMessage("Data saved successfully.");
                }
                catch (Exception ex)
                {
                    UpdateDataGridMessage($"Error saving data: {ex.Message}");
                }
            }
        }

        private async void OnConnectClick(object sender, RoutedEventArgs e)
        {
            string address = addressTextBox.Text;
            string[] parts = address.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[1], out int port))
            {
                await tcpManager.ConnectAsync(parts[0], port);
            }
            else
            {
                uiManager.UpdateConfirmationMessage("Invalid address format. Use IP:Port");
            }
        }

        private async void OnDisconnectClick(object sender, RoutedEventArgs e)
        {
            await tcpManager.DisconnectAsync();
            ResetTimer();
            if (isWaitingForConfirm)
            {
                isWaitingForConfirm = false;
                uiManager.UpdateConfirmationMessage("Disconnected while awaiting confirmation.");
            }
        }

        private void HandleMessageReceived(string message, string? clientId)
        {
            if (message == "CONFIRM")
            {
                Dispatcher.Invoke(() =>
                {
                    if (isWaitingForConfirm)
                    {
                        isWaitingForConfirm = false;
                        uiManager.UpdateConfirmationMessage("Data received by server.");
                    }
                    else
                    {
                        uiManager.UpdateConfirmationMessage("Received unexpected CONFIRM message.");
                    }
                });
            }
            else if (message.StartsWith("TIMER:"))
            {
                try
                {
                    string time = message.Substring(6);
                    if (TimeSpan.TryParse(time, out TimeSpan newTimerValue))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (dispatcherTimer.IsEnabled && timerValue.ToString(@"hh\:mm\:ss") != time)
                            {
                                dispatcherTimer.Stop();
                            }
                            timerValue = newTimerValue;
                            uiManager.UpdateTimerLabel(time);
                            timerTextBox.Text = time;
                            string state = newTimerValue > TimeSpan.Zero ? "Ticking" : "Default";
                            uiManager.UpdateTimerButtons(state, newTimerValue);
                        });
                    }
                }
                catch (Exception)
                {
                    // Ignore parsing errors
                }
            }
            else if (message == "STOP")
            {
                Dispatcher.Invoke(() =>
                {
                    dispatcherTimer.Stop();
                    uiManager.UpdateTimerButtons("Stopped", timerValue);
                    uiManager.UpdateConfirmationMessage("Timer stopped by another client.");
                });
            }
            else if (message == "RESET")
            {
                Dispatcher.Invoke(() => ResetTimer());
            }
        }

        private async void OnSendClick(object sender, RoutedEventArgs e)
        {
            if (DynamicDataGrid.SelectedItem != null && DynamicDataGrid.SelectedItem is Record selectedRecord)
            {
                if (isWaitingForConfirm)
                {
                    uiManager.UpdateConfirmationMessage("Please wait for previous data confirmation.");
                    return;
                }

                try
                {
                    var payload = new
                    {
                        Columns = columnNames.ToList(),
                        Record = selectedRecord.Properties
                    };
                    string json = JsonConvert.SerializeObject(payload);
                    string message = "DATA:" + json;
                    isWaitingForConfirm = true;
                    uiManager.UpdateConfirmationMessage("Data sent, awaiting confirmation.");

                    Task sendTask = tcpManager.SendMessageAsync(message);
                    Task timeoutTask = Task.Delay(5000);
                    Task completedTask = await Task.WhenAny(sendTask, timeoutTask);

                    if (completedTask == timeoutTask)
                    {
                        isWaitingForConfirm = false;
                        uiManager.UpdateConfirmationMessage("Data send timed out, no confirmation received.");
                        return;
                    }

                    await sendTask;
                }
                catch (Exception ex)
                {
                    isWaitingForConfirm = false;
                    uiManager.UpdateConfirmationMessage($"Error sending data: {ex.Message}");
                }
            }
            else
            {
                uiManager.UpdateConfirmationMessage("Cannot send data: No record selected or not connected.");
            }
        }

        private void OnSetTimerClick(object sender, RoutedEventArgs e)
        {
            string[] parts = timerTextBox.Text.Split(':');
            if (parts.Length != 3 || !int.TryParse(parts[0], out int hours) ||
                !int.TryParse(parts[1], out int minutes) || !int.TryParse(parts[2], out int seconds))
            {
                uiManager.UpdateConfirmationMessage("Invalid timer format.");
                return;
            }

            timerValue = new TimeSpan(hours, minutes, seconds);
            uiManager.UpdateTimerLabel(timerValue.ToString(@"hh\:mm\:ss"));
            uiManager.UpdateTimerButtons("Set", timerValue);
        }

        private async void OnStartTimerClick(object sender, RoutedEventArgs e)
        {
            if (timerValue > TimeSpan.Zero && tcpManager.IsConnected)
            {
                try
                {
                    dispatcherTimer.Start();
                    uiManager.UpdateTimerButtons("Ticking", timerValue);
                    uiManager.UpdateConfirmationMessage("Timer started.");
                    string message = $"TIMER:{timerValue.ToString(@"hh\:mm\:ss")}";
                    await tcpManager.SendMessageAsync(message);
                }
                catch (Exception ex)
                {
                    uiManager.UpdateConfirmationMessage($"Error starting timer: {ex.Message}");
                }
            }
            else
            {
                uiManager.UpdateConfirmationMessage("Cannot start timer: No time set or not connected.");
            }
        }

        private async void OnStopTimerClick(object sender, RoutedEventArgs e)
        {
            if (dispatcherTimer.IsEnabled)
            {
                dispatcherTimer.Stop();
                uiManager.UpdateTimerButtons("Stopped", timerValue);
                uiManager.UpdateConfirmationMessage("Timer stopped.");
            }
            else if (timerValue > TimeSpan.Zero && tcpManager.IsConnected)
            {
                try
                {
                    await tcpManager.SendMessageAsync("STOP");
                    uiManager.UpdateTimerButtons("Stopped", timerValue);
                    uiManager.UpdateConfirmationMessage("Timer stop requested.");
                }
                catch (Exception ex)
                {
                    uiManager.UpdateConfirmationMessage($"Error sending stop message: {ex.Message}");
                }
            }
            else
            {
                uiManager.UpdateConfirmationMessage("Timer is not running or no time set.");
            }
        }

        private async void OnResumeTimerClick(object sender, RoutedEventArgs e)
        {
            if (timerValue > TimeSpan.Zero && tcpManager.IsConnected)
            {
                try
                {
                    dispatcherTimer.Start();
                    uiManager.UpdateTimerButtons("Ticking", timerValue);
                    uiManager.UpdateConfirmationMessage("Timer resumed.");
                    string message = $"TIMER:{timerValue.ToString(@"hh\:mm\:ss")}";
                    await tcpManager.SendMessageAsync(message);
                }
                catch (Exception ex)
                {
                    uiManager.UpdateConfirmationMessage($"Error resuming timer: {ex.Message}");
                }
            }
            else
            {
                uiManager.UpdateConfirmationMessage("Cannot resume timer: No time set or not connected.");
            }
        }

        private async void OnResetTimerClick(object sender, RoutedEventArgs e)
        {
            ResetTimer();
            if (tcpManager.IsConnected)
            {
                try
                {
                    await tcpManager.SendMessageAsync("RESET");
                    uiManager.UpdateConfirmationMessage("Timer reset sent.");
                }
                catch (Exception ex)
                {
                    uiManager.UpdateConfirmationMessage($"Error sending reset message: {ex.Message}");
                }
            }
        }

        private void ResetTimer()
        {
            dispatcherTimer.Stop();
            timerValue = TimeSpan.Zero;
            timerTextBox.Text = "00:00:00";
            uiManager.UpdateTimerLabel("00:00:00");
            uiManager.UpdateTimerButtons("Default", timerValue);
        }

        private async void OnTimerTick(object? sender, EventArgs e)
        {
            if (timerValue > TimeSpan.Zero)
            {
                timerValue = timerValue.Subtract(TimeSpan.FromSeconds(1));
                uiManager.UpdateTimerLabel(timerValue.ToString(@"hh\:mm\:ss"));
                timerTextBox.Text = timerValue.ToString(@"hh\:mm\:ss");
                uiManager.UpdateTimerButtons("Ticking", timerValue);

                if (tcpManager.IsConnected)
                {
                    try
                    {
                        string message = $"TIMER:{timerValue.ToString(@"hh\:mm\:ss")}";
                        await tcpManager.SendMessageAsync(message);
                    }
                    catch (Exception ex)
                    {
                        uiManager.UpdateConfirmationMessage($"Error sending timer update: {ex.Message}");
                        dispatcherTimer.Stop();
                        uiManager.UpdateTimerButtons("Stopped", timerValue);
                    }
                }
            }
            else
            {
                dispatcherTimer.Stop();
                uiManager.UpdateTimerButtons("Default", timerValue);
                uiManager.UpdateConfirmationMessage("Timer finished.");
            }
        }
    }
}