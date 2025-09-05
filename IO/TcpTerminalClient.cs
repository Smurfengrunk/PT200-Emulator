using PT200Emulator.IO;
using PT200Emulator.Parser;
using PT200Emulator.Protocol;
using PT200Emulator.Util;
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using static PT200Emulator.Util.Logger;

public class TcpTerminalClient : ITerminalClient, IDisposable
{
    private TcpClient _client;
    private NetworkStream _stream;
    private TelnetInterpreter _telnet;
    private EscapeSequenceParser _parser;

    public async Task ConnectAsync(string host, int port)
    {
        _client = new TcpClient();
        await _client.ConnectAsync(host, port);
        _stream = _client.GetStream();
        Logger.Log($"Connected to {host}:{port}", Logger.LogLevel.Info);
    }

    public event Action<string> DataReceived;

    private void RaiseDataReceived(string text)
    {
        DataReceived?.Invoke(text);
    }


    public TcpTerminalClient(TcpClient client, EscapeSequenceParser parser)
    {
        _client = client;
        _stream = client.GetStream();
        _parser = parser;
        _telnet = new TelnetInterpreter();

        _telnet.OnDataByte += async b =>
        {
            char ch = (char)b;
            Logger.Log($"TO-PARSER: 0x{b:X2} '{ch}'", Logger.LogLevel.Debug);
            await _parser.Feed(ch);
            RaiseDataReceived(ch.ToString());
        };

        _telnet.OnTelnetCommand += cmd =>
        {
            Logger.Log($"TELNET: {cmd}", Logger.LogLevel.Info);
        };
    }

    public async Task StartAsync(CancellationToken token)
    {
        byte[] buffer = new byte[1024];
        while (_stream.CanRead && !token.IsCancellationRequested)
        {
            int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, token);
            if (bytesRead > 0)
            {
                Logger.LogHex(buffer, bytesRead, "RX");
                _telnet.Feed(buffer, bytesRead);
            }
        }
    }

    public async Task SendAsync(char ch)
    {
        Logger.Log("tcpClient.SendAsync startar", LogLevel.Debug);
        Logger.Log($"SendAsync körs på instans – Hash: {this.GetHashCode()}", LogLevel.Info);

        byte[] buffer = new byte[] { (byte)ch };
        await _stream.WriteAsync(buffer, 0, buffer.Length);
        Logger.Log($"TX: 0x{buffer[0]:X2} '{ch}'", Logger.LogLevel.Debug);

        Logger.Log("tcpClient.SendAsync avslutas", LogLevel.Debug);

    }

    public async Task SendAsync(string text)
    {
        Logger.Log("tcpClient.SendAsync startar", LogLevel.Debug);
        Logger.Log($"SendAsync körs på instans – Hash: {this.GetHashCode()}", LogLevel.Info);

        byte[] buffer = Encoding.ASCII.GetBytes(text);
        await _stream.WriteAsync(buffer, 0, buffer.Length);
        Logger.Log($"TX: {BitConverter.ToString(buffer)} \"{text}\"", Logger.LogLevel.Debug);

        Logger.Log("tcpClient.SendAsync avslutas", LogLevel.Debug);

    }

    public async Task SendAsync(byte[] buffer)
    {
        Logger.Log("tcpClient.SendAsync startar", LogLevel.Debug);
        Logger.Log($"SendAsync körs på instans – Hash: {this.GetHashCode()}", LogLevel.Info);

        if (buffer == null || buffer.Length == 0)
            return;

        await _stream.WriteAsync(buffer, 0, buffer.Length);
        Logger.Log($"TX: {BitConverter.ToString(buffer)}", Logger.LogLevel.Debug);

        Logger.Log("tcpClient.SendAsync avslutas", LogLevel.Debug);

    }

    public void Dispose()
    {
        _stream?.Close();
        _client?.Close();
        Logger.Log("TcpTerminalClient har stängts", Logger.LogLevel.Info);
    }
}