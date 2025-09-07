using PT200Emulator.Core;
using PT200Emulator.IO;
using PT200Emulator.Parser;
using PT200Emulator.Util;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using static PT200Emulator.Util.Logger;

public class TerminalSessionManager
{
    private readonly IPAddress _host;
    private readonly int _port;
    private readonly IScreenBuffer _screenBuffer;
    private readonly Action<string> onDataReceived;
    private readonly Action onLayoutUpdated;

    private TcpTerminalClient _client;
    private EscapeSequenceParser _parser;
    private CancellationTokenSource _cts;

    public bool IsConnected { get; private set; }

    public TerminalSessionManager(
    IPAddress host,
    int port,
    IScreenBuffer screenBuffer,
    Action<string> onDataReceived,
    Action onLayoutUpdated)
    {
        _host = host;
        _port = port;
        _screenBuffer = screenBuffer;
        this.onDataReceived = onDataReceived;
        this.onLayoutUpdated = onLayoutUpdated;
    }
    public async Task<bool> ConnectAsync()
    {
        try
        {
            var rawClient = new TcpClient();
            await rawClient.ConnectAsync(_host, _port);
            _parser = new EscapeSequenceParser(_screenBuffer);
            Logger.Log("🔧 Skapar TcpTerminalClient...", Logger.LogLevel.Info);
            var client = new TcpTerminalClient(rawClient, _parser);
            Logger.Log($"✅ TcpTerminalClient skapad – Hash: {client.GetHashCode()}", Logger.LogLevel.Info);
            _client = client;
            Logger.Log($"📌 _client tilldelad – Hash: {_client?.GetHashCode() ?? -1}", Logger.LogLevel.Info); Logger.Log($"[ConnectAsync] _client satt? {_client != null}", Logger.LogLevel.Info);
            _parser.SetClient(_client);

            _parser.OutgoingDcs += async bytes => await ((TcpTerminalClient)client).SendAsync(bytes);
            _parser.OutgoingRaw += async bytes =>
            {
                Logger.Log($"OutgoingRaw triggas – {bytes.Length} byte", Logger.LogLevel.Debug);
                await ((TcpTerminalClient)client).SendAsync(bytes);
            };
            _parser.EmacsLayoutUpdated += onLayoutUpdated;
            _client.DataReceived += onDataReceived;

            IsConnected = true;
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"Misslyckad anslutning: {ex.Message}", Logger.LogLevel.Error);
            IsConnected = false;
            return false;
        }
    }

    public async Task StartSessionAsync()
    {
        Logger.Log("Startar terminalsession...", Logger.LogLevel.Info);
        _cts = new CancellationTokenSource();
        var readTask = _client.StartAsync(_cts.Token);
        if (await Task.WhenAny(readTask, Task.Delay(5000)) == readTask)
        {
            Logger.Log("✅ Klienten startade", Logger.LogLevel.Info);
            // hantera data
        }
        else
        {
            Logger.Log("⏳ Timeout – ingen data från servern", LogLevel.Warning);
        }
        //await _client.StartAsync(_cts.Token);
    }

    public void StopSession()
    {
        Logger.Log("tcpClient nollas – sessionen stängs", LogLevel.Warning);
        _cts?.Cancel();
        _client?.Dispose();
        IsConnected = false;
    }

    public string GetSessionStatus()
    {
        return $"Connected: {IsConnected}, Parser: {(Parser != null ? "OK" : "NULL")}, Client: {(Client != null ? "OK" : "NULL")}";
    }

    public async Task SendBytes(byte[] data)
    {
        await Client.SendAsync(data); // eller vad din klientmetod heter
    }

    public ITerminalClient Client
    {
        get
        {
            Logger.Log($"[Client getter] returnerar: {_client?.GetHashCode() ?? -1}", Logger.LogLevel.Debug);
            return _client;
        }
    }
    public EscapeSequenceParser Parser => _parser;
}