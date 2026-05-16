using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace LanFileTransfer.Services;

public class DiscoveryService
{
    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private const int DiscoveryPort = 8889;
    private const string DiscoveryMessagePrefix = "LAN_FILE_TRANSFER_DISCOVERY";

    public event Action<string, int, string>? OnDeviceDiscovered;
    public event Action<string>? OnLog;

    public bool IsRunning => _udpClient != null;

    public async Task StartBroadcastingAsync(int httpPort)
    {
        _cts = new CancellationTokenSource();
        _udpClient = new UdpClient();
        _udpClient.EnableBroadcast = true;

        var localIp = HttpFileServer.GetLocalIPAddress();
        var machineName = Environment.MachineName;

        var discoveryData = new
        {
            type = DiscoveryMessagePrefix,
            serverName = machineName,
            ip = localIp,
            port = httpPort,
            version = "1.0.0"
        };

        var json = JsonSerializer.Serialize(discoveryData);
        var data = Encoding.UTF8.GetBytes(json);

        Log($"发现服务已启动，广播地址: 255.255.255.255:{DiscoveryPort}");

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                await _udpClient.SendAsync(data, data.Length,
                    new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
                await Task.Delay(3000, _cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log($"广播异常: {ex.Message}");
        }
    }

    public async Task StartListeningAsync()
    {
        var listenClient = new UdpClient(new IPEndPoint(IPAddress.Any, DiscoveryPort));

        try
        {
            while (_cts?.IsCancellationRequested != true)
            {
                var result = await listenClient.ReceiveAsync(_cts?.Token ?? CancellationToken.None);
                var json = Encoding.UTF8.GetString(result.Buffer);

                try
                {
                    var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("type", out var typeProp) &&
                        typeProp.GetString() == DiscoveryMessagePrefix)
                    {
                        var serverName = root.TryGetProperty("serverName", out var sn) ? sn.GetString() ?? "" : "";
                        var ip = root.TryGetProperty("ip", out var ipProp) ? ipProp.GetString() ?? "" : "";
                        var port = root.TryGetProperty("port", out var p) ? p.GetInt32() : 0;

                        if (!string.IsNullOrEmpty(ip) && port > 0)
                        {
                            OnDeviceDiscovered?.Invoke(serverName, port, ip);
                        }
                    }
                }
                catch (JsonException) { }
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            Log($"监听异常: {ex.Message}");
        }
        finally
        {
            listenClient.Close();
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _udpClient?.Close();
        _udpClient = null;
        Log("发现服务已停止");
    }

    private void Log(string message)
    {
        OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");
    }
}