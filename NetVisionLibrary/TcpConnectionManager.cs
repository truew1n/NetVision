using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace NetVisionLibrary
{
    public class TcpConnectionManager
    {
        private TcpClient? _client;
        private TcpListener? _server;
        private readonly bool _isServer;
        private NetworkStream? _stream;
        private readonly ConcurrentDictionary<string, (TcpClient client, NetworkStream stream)> _clients;
        private bool _isRunning;
        public int ClientCount => _isServer ? _clients.Count : (_client != null && _client.Connected ? 1 : 0);
        public bool IsConnected => _isServer ? _clients.Count > 0 : (_client != null && _client.Connected);

        public event Action<string, string?>? OnMessageReceived;
        public event Action<bool, string, int>? OnConnectionStateChanged;

        public TcpConnectionManager(bool isServer)
        {
            _isServer = isServer;
            _clients = new ConcurrentDictionary<string, (TcpClient client, NetworkStream stream)>();
            _isRunning = false;
        }

        public async Task StartListeningAsync(int port)
        {
            if (!_isServer) throw new InvalidOperationException("StartListeningAsync is only for server mode.");
            try
            {
                _server = new TcpListener(IPAddress.Any, port);
                _server.Start();
                _isRunning = true;
                OnConnectionStateChanged?.Invoke(true, "Server started.", 0);

                while (_isRunning)
                {
                    var client = await _server.AcceptTcpClientAsync();
                    string clientId = Guid.NewGuid().ToString();
                    var stream = client.GetStream();
                    _clients.TryAdd(clientId, (client, stream));
                    OnConnectionStateChanged?.Invoke(true, "Client connected.", _clients.Count);

                    _ = Task.Run(() => HandleClientAsync(client, clientId, stream));
                }
            }
            catch (Exception ex)
            {
                OnConnectionStateChanged?.Invoke(false, $"Server error: {ex.Message}", _clients.Count);
            }
        }

        public async Task ConnectAsync(string ip, int port)
        {
            if (_isServer) throw new InvalidOperationException("ConnectAsync is only for client mode.");
            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(ip, port);
                _stream = _client.GetStream();
                _isRunning = true;
                OnConnectionStateChanged?.Invoke(true, "Connected to server.", 1);

                _ = Task.Run(() => HandleClientAsync(_client, null, _stream));
            }
            catch (Exception ex)
            {
                OnConnectionStateChanged?.Invoke(false, $"Connection error: {ex.Message}", 0);
            }
        }

        public Task DisconnectAsync()
        {
            _isRunning = false;
            if (_isServer)
            {
                foreach (var client in _clients)
                {
                    client.Value.stream.Close();
                    client.Value.client.Close();
                }
                _clients.Clear();
                _server?.Stop();
                OnConnectionStateChanged?.Invoke(false, "Server stopped.", 0);
            }
            else
            {
                _stream?.Close();
                _client?.Close();
                OnConnectionStateChanged?.Invoke(false, "Disconnected from server.", 0);
            }
            return Task.CompletedTask;
        }

        public async Task SendMessageAsync(string message)
        {
            if (_isServer)
            {
                foreach (var client in _clients)
                {
                    await SendMessageToClientAsync(message, client.Key);
                }
            }
            else
            {
                if (_stream == null || !_client!.Connected)
                {
                    throw new InvalidOperationException("Not connected.");
                }
                byte[] buffer = Encoding.UTF8.GetBytes(message + "\n");
                await _stream.WriteAsync(buffer, 0, buffer.Length);
            }
        }

        public async Task SendMessageToClientAsync(string message, string? clientId)
        {
            if (!_isServer || clientId == null)
            {
                return;
            }
            if (_clients.TryGetValue(clientId, out var clientInfo) && clientInfo.client.Connected)
            {
                try
                {
                    byte[] buffer = Encoding.UTF8.GetBytes(message + "\n");
                    await clientInfo.stream.WriteAsync(buffer, 0, buffer.Length);
                }
                catch (Exception)
                {
                    RemoveClient(clientId);
                }
            }
        }

        public async Task SendMessageToOthersAsync(string message, string? senderClientId)
        {
            if (!_isServer)
            {
                return;
            }
            foreach (var client in _clients)
            {
                if (client.Key != senderClientId)
                {
                    await SendMessageToClientAsync(message, client.Key);
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, string? clientId, NetworkStream stream)
        {
            try
            {
                using (var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, true))
                {
                    while (_isRunning && client.Connected)
                    {
                        string? message = await reader.ReadLineAsync();
                        if (message != null)
                        {
                            OnMessageReceived?.Invoke(message, clientId);
                        }
                        else
                        {
                            break; // Client disconnected
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Ignore errors
            }
            finally
            {
                if (_isServer && clientId != null)
                {
                    RemoveClient(clientId);
                }
                else if (!_isServer)
                {
                    _stream?.Close();
                    _client?.Close();
                    OnConnectionStateChanged?.Invoke(false, "Disconnected from server.", 0);
                }
            }
        }

        private void RemoveClient(string clientId)
        {
            if (_clients.TryRemove(clientId, out var clientInfo))
            {
                clientInfo.stream.Close();
                clientInfo.client.Close();
                OnConnectionStateChanged?.Invoke(true, "Client disconnected.", _clients.Count);
            }
        }
    }
}