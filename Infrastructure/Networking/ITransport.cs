using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PT200Emulator.Infrastructure.Networking
{
    public interface ITransport : IAsyncDisposable
    {
        Task ConnectAsync(string host, int port, CancellationToken cancellationToken);
        Task SendAsync(byte[] data, CancellationToken cancellationToken, string host, int port);
        Task DisconnectAsync();
        bool IsConnected { get; }
        public event Action Reconnected;
        public event Action Disconnected;
        public event Action<byte[], int> OnDataReceived;
        public event Action<string> OnStatusUpdate;
        public int GetDataReceivedHandlerCount();
        public Task ReconnectAsync(string host, int port);
    }
}