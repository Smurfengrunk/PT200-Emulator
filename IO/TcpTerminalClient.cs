using PT200Emulator.IO;
using PT200Emulator.Parser;
using PT200Emulator.Protocol;
using PT200Emulator.Util;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using static PT200Emulator.Util.Logger;

public class TcpTerminalClient : ITerminalClient, IDisposable
{
    internal TcpClient _client;
    private NetworkStream _stream;
    private TelnetInterpreter _telnet;
    private EscapeSequenceParser _parser;
    private TelnetSessionBridge _telnetBridge;
    public bool Connected => !(_client.Client.Poll(1, SelectMode.SelectRead) && _client.Client.Available == 0);

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

        _telnetBridge = new TelnetSessionBridge(
            _telnet,
            bytes => _ = SendAsync(bytes),          // skickar till servern
            ch => RaiseDataReceived(ch.ToString()),           // renderar tecken
            msg => Logger.Log(msg, Logger.LogLevel.Debug) // loggar händelser
        );

        _telnet.OnTelnetCommand += cmd =>
        {
            Logger.Log($"TELNET: {cmd}", Logger.LogLevel.Info);
        };
        _telnet.OnDataByte += async b => await parser.Feed((char)b);
    }

    public async Task StartAsync(CancellationToken token)
    {
        int bytesRead = 0;
        byte[] buffer = new byte[1024];
        Logger.Log($"🧵 [TcpTerminalClient] Körs på tråd: {Thread.CurrentThread.ManagedThreadId}", LogLevel.Trace);
        try
        {
            while (_stream.CanRead && !token.IsCancellationRequested)
            {
                Logger.Log("📥 Väntar på data från servern...", LogLevel.Debug);
                var readTask = _stream.ReadAsync(buffer, 0, buffer.Length);
                if (await Task.WhenAny(readTask, Task.Delay(5000)) == readTask)
                {
                    bytesRead = await readTask;
                    Logger.Log($"📥 Mottog {bytesRead} byte", LogLevel.Debug);
                    Logger.LogHex(buffer, bytesRead, "RX RAW");
                    Logger.Log($"RX ASCII: \"{Encoding.ASCII.GetString(buffer, 0, bytesRead)}\"");

                    if (bytesRead == 0)
                    {
                        Logger.Log("🔌 Ingen data – servern har stängt anslutningen", LogLevel.Info);
                        break;
                    }
                    string asciiClean = Encoding.ASCII.GetString(buffer, 0, bytesRead)
                        .Replace("\r", "\\r")
                        .Replace("\n", "\\n");
                    Logger.Log($"📥 RX ASCII: \"{asciiClean}\"", Logger.LogLevel.Debug);
                    Logger.Log($"🧠 [Parser] Matar in {bytesRead} byte till Feed()", Logger.LogLevel.Trace);
                    Logger.Log($"📥 RX ASCII: \"{asciiClean}\"", Logger.LogLevel.Trace);
                    _telnet.Feed(buffer, bytesRead);

                }
                else
                {
                    Logger.Log("⏳ Timeout – ingen data från servern på 5 sekunder", LogLevel.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"💥 Undantag i StartAsync: {ex.Message}", LogLevel.Error);
        }

        var asciiText = BitConverter.ToString(buffer, 0, bytesRead);
        if (asciiText.Contains("Prime session disconnected"))
        {
            Logger.Log("🔚 Servern har avslutat sessionen", LogLevel.Info);
            // Stäng klient, uppdatera UI, etc.
        }
        if (bytesRead > 0)
        {
            Logger.LogHex(buffer, bytesRead, "RX");
            Logger.Log($"📥 [TcpTerminalClient] Mottog {bytesRead} byte från servern", Logger.LogLevel.Debug);
            Logger.Log($"📥 RX HEX: {BitConverter.ToString(buffer, 0, bytesRead)}", Logger.LogLevel.Trace);
        }
    }

    public async Task SendAsync(char ch)
    {
        byte[] buffer = new byte[] { (byte)ch };
        Logger.Log("tcpClient.SendAsync startar", LogLevel.Debug);
        Logger.LogHex(buffer, buffer.Length, "TX");
        Logger.Log($"📤 SEND: {BitConverter.ToString(buffer, 0, buffer.Length)}");
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
        Logger.LogHex(buffer, buffer.Length, "TX");
        Logger.Log($"📤 SEND: {BitConverter.ToString(buffer, 0, buffer.Length)}");
        Logger.Log("tcpClient.SendAsync avslutas", LogLevel.Debug);

    }

    public async Task SendAsync(byte[] buffer)
    {
        Logger.Log("tcpClient.SendAsync startar", LogLevel.Debug);
        Logger.Log($"SendAsync körs på instans – Hash: {this.GetHashCode()}", LogLevel.Info);

        if (buffer == null || buffer.Length == 0)
            return;

        await _stream.WriteAsync(buffer, 0, buffer.Length);
        Logger.LogHex(buffer, buffer.Length, "TX");
        Logger.Log($"📤 SEND: {BitConverter.ToString(buffer, 0, buffer.Length)}");
        Logger.Log("tcpClient.SendAsync avslutas", LogLevel.Debug);

    }

    public void Dispose()
    {
        _stream?.Close();
        _client?.Close();
        Logger.Log("TcpTerminalClient har stängts", Logger.LogLevel.Info);
    }
}