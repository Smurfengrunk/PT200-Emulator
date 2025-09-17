using Microsoft.Extensions.Logging;
using PT200Emulator.Infrastructure.Logging;
using PT200Emulator.Infrastructure.Networking;
using PT200Emulator.UI;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows;

namespace PT200Emulator.Infrastructure.Networking
{
    public class TcpClientTransport : ITransport, IAsyncDisposable
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private CancellationTokenSource _cts;
        private string _host;
        private int _port;
        private bool _manualDisconnect = false;
        private bool _isReconnecting = false;

        public bool IsConnected { get; private set; }

        public event Action Reconnected;
        public event Action Disconnected;
        public event Action<byte[], int> OnDataReceived;
        public event Action<string> OnStatusUpdate;

        public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
        {
            _manualDisconnect = false;
            _host = host;
            _port = port;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            this.LogInformation($"Försöker ansluta till {host} {port}");
            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(host, port, cancellationToken);
                _stream = _client.GetStream();
                IsConnected = true;
                OnStatusUpdate?.Invoke("🟢 Ansluten");

                _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
                this.LogInformation($"Ansluten till {host} {port}");
            }
            catch (Exception ex)
            {
                OnStatusUpdate?.Invoke($"🔴 Kunde inte ansluta: {ex.Message}");
                throw;
            }
        }

        public async Task DisconnectAsync()
        {
            _manualDisconnect = true;
            _cts?.Cancel();

            if (_stream != null)
            {
                await _stream.DisposeAsync();
                _stream = null;
            }

            _client?.Close();
            _client = null;
            IsConnected = false;

            Disconnected?.Invoke();
            OnStatusUpdate?.Invoke("🔴 Frånkopplad");
        }

        public async Task SendAsync(byte[] data, CancellationToken cancellationToken)
        {
            if (_stream == null) return;

            try
            {
                //this.LogDebug($"[SENDASYNC] data[0] = {data[0]}");
                await _stream.WriteAsync(data, 0, data.Length, cancellationToken);
                await _stream.FlushAsync(cancellationToken);
            }
            catch
            {
                await TryReconnectAsync();
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    int bytesRead = await _stream!.ReadAsync(buffer, 0, buffer.Length, cancellationToken);

                    if (bytesRead == 0)
                    {
                        await HandleDisconnectAsync();
                        break;
                    }

                    var chunk = new byte[bytesRead];
                    Array.Copy(buffer, chunk, bytesRead);
                    OnDataReceived?.Invoke(chunk, chunk.Length);
                    this.LogDebug($"[RAW IN] {BitConverter.ToString(chunk)}");
                }
            }
            catch
            {
                await HandleDisconnectAsync();
            }
        }

        private async Task HandleDisconnectAsync()
        {
            IsConnected = false;
            Disconnected?.Invoke();
            OnStatusUpdate?.Invoke("🔴 Anslutning tappad");
            await TryReconnectAsync();
        }

        private async Task TryReconnectAsync()
        {
            if (_manualDisconnect || _isReconnecting || string.IsNullOrEmpty(_host)) return;

            _isReconnecting = true;
            OnStatusUpdate?.Invoke("🔄 Försöker återansluta...");

            for (int i = 0; i < 5; i++)
            {
                try
                {
                    await Task.Delay(3000);
                    await ConnectAsync(_host!, _port, CancellationToken.None);
                    Reconnected?.Invoke();
                    return;
                }
                catch
                {
                    OnStatusUpdate?.Invoke($"⚠️ Försök {i + 1} misslyckades");
                }
            }

            OnStatusUpdate?.Invoke("❌ Kunde inte återansluta");
            _isReconnecting = false;
        }

        public async ValueTask DisposeAsync()
        {
            await DisconnectAsync();
            _cts?.Dispose();
        }
    }
}