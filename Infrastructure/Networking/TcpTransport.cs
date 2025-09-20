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
        public int GetDataReceivedHandlerCount()
        {
            return OnDataReceived?.GetInvocationList().Length ?? 0;
        }
        public event Action<string> OnStatusUpdate;

        private Task _receiveLoopTask;
        private bool _receiveLoopRunning;

        public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
        {
            this.LogTrace($"[TcpClientTransport] ConnectAsync start – host: {host}, port: {port}");
            if (_receiveLoopRunning)
                await DisconnectAsync(); // Vänta ut gammal loop

            _manualDisconnect = false;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            this.LogTrace($"Försöker ansluta till {host} {port}");
            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(host, port, cancellationToken);
                _stream = _client.GetStream();
                IsConnected = true;
                OnStatusUpdate?.Invoke("🟢 Ansluten");

                _receiveLoopRunning = true;
                _receiveLoopTask = ReceiveLoopAsync(_cts.Token, host, port);
                this.LogInformation($"Ansluten till {host} {port}");
            }
            catch (Exception ex)
            {
                OnStatusUpdate?.Invoke($"🔴 Kunde inte ansluta: {ex.Message}");
                throw;
            }
            _host = host;
            _port = port;
        }

        public async Task DisconnectAsync()
        {
            _manualDisconnect = true;
            _cts?.Cancel();

            if (_stream != null)
            {
                await _stream.DisposeAsync(); // ← Tvinga ReadAsync att kasta
                _stream = null;
            }

            if (_receiveLoopTask != null)
            {
                try
                {
                    await _receiveLoopTask;
                }
                catch (OperationCanceledException) { }
                _receiveLoopTask = null;
            }

            _client?.Close();
            _client = null;
            IsConnected = false;

            Disconnected?.Invoke();
            OnStatusUpdate?.Invoke("🔴 Frånkopplad");
        }

        public async Task SendAsync(byte[] data, CancellationToken cancellationToken, string host, int port)
        {
            if (_stream == null) return;

            try
            {
                await _stream.WriteAsync(data, 0, data.Length, cancellationToken);
                await _stream.FlushAsync(cancellationToken);
            }
            catch
            {
                await TryReconnectAsync(host, port);
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken, string host, int port)
        {
            this.LogTrace($"[TcpClientTransport] ReceiveLoopAsync start – hash: {GetHashCode()}, task: {Task.CurrentId}");
            var buffer = new byte[4096];

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    int bytesRead = await _stream!.ReadAsync(buffer, 0, buffer.Length, cancellationToken);

                    if (bytesRead == 0)
                    {
                        await HandleDisconnectAsync(host, port);
                        break;
                    }

                    var chunk = new byte[bytesRead];
                    Array.Copy(buffer, chunk, bytesRead);
                    this.LogTrace($"[TcpClientTransport] Invoking OnDataReceived – chunk length: {chunk.Length}, handler count: {OnDataReceived?.GetInvocationList().Length}");
                    OnDataReceived?.Invoke(chunk, chunk.Length);
                    this.LogTrace($"[RAW IN] {BitConverter.ToString(chunk)}");
                }
            }
            catch
            {
                await HandleDisconnectAsync(host, port);
            }
            finally
            {
                _receiveLoopRunning = false;
                this.LogTrace($"[TcpClientTransport] ReceiveLoopAsync avslutad – hash: {GetHashCode()}");
            }
        }

        private async Task HandleDisconnectAsync(string host, int port)
        {
            IsConnected = false;
            Disconnected?.Invoke();
            OnStatusUpdate?.Invoke("🔴 Anslutning tappad");
            await TryReconnectAsync(host, port);
        }

        private async Task TryReconnectAsync(string host, int port)
        {
            if (_manualDisconnect || _isReconnecting || string.IsNullOrEmpty(host)) return;

            _isReconnecting = true;
            OnStatusUpdate?.Invoke("🔄 Försöker återansluta...");

            for (int i = 0; i < 5; i++)
            {
                try
                {
                    await Task.Delay(3000);
                    await ConnectAsync(host, port, CancellationToken.None);
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

        public async Task ReconnectAsync(string host, int port)
        {
            _manualDisconnect = false;
            _isReconnecting = true;
            OnStatusUpdate?.Invoke($"🔁 Manuell återanslutning till {host}:{port}...");

            try
            {
                await DisconnectAsync(); // säkerställ att gammal loop är död
                await ConnectAsync(host, port, CancellationToken.None); // starta ny loop
                Reconnected?.Invoke();
                OnStatusUpdate?.Invoke("🟢 Återansluten");
            }
            catch (Exception ex)
            {
                OnStatusUpdate?.Invoke($"❌ Återanslutning misslyckades: {ex.Message}");
                throw;
            }
            finally
            {
                _isReconnecting = false;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await DisconnectAsync();
            _cts?.Dispose();
        }
    }
}