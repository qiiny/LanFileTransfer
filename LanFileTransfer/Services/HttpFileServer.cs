using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LanFileTransfer.Models;

namespace LanFileTransfer.Services;

public class HttpFileServer
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, FileItem> _sharedFiles = new();
    private readonly ConcurrentQueue<TransferLog> _transferLogs = new();
    private string _downloadPath;
    private int _port;

    public event Action<string>? OnLog;
    public event Action<TransferLog>? OnTransferComplete;
    public event Action? OnServerStarted;
    public event Action? OnServerStopped;

    public bool IsRunning => _listener?.IsListening ?? false;
    public int Port => _port;
    public string DownloadPath => _downloadPath;

    public void SetDownloadPath(string path)
    {
        _downloadPath = path;
        Directory.CreateDirectory(_downloadPath);
    }

    public void SetPort(int port)
    {
        _port = port;
    }

    public HttpFileServer(string downloadPath, int port = 8888)
    {
        _downloadPath = downloadPath;
        _port = port;
        Directory.CreateDirectory(downloadPath);
    }

    public IReadOnlyList<FileItem> SharedFiles => _sharedFiles.Values.ToList().AsReadOnly();
    public IReadOnlyList<TransferLog> TransferLogs => _transferLogs.ToList().AsReadOnly();

    public void AddSharedFile(string filePath)
    {
        if (!File.Exists(filePath)) return;

        var fileInfo = new FileInfo(filePath);
        var item = new FileItem
        {
            FileName = fileInfo.Name,
            FilePath = filePath,
            FileSize = fileInfo.Length,
            FileType = fileInfo.Extension.TrimStart('.').ToUpperInvariant(),
            AddedTime = DateTime.Now
        };

        _sharedFiles[item.Id] = item;
        Log($"已添加共享文件: {item.FileName}");
    }

    public void RemoveSharedFile(string id)
    {
        if (_sharedFiles.TryRemove(id, out var item))
        {
            Log($"已移除共享文件: {item.FileName}");
        }
    }

    public void ClearSharedFiles()
    {
        _sharedFiles.Clear();
        Log("已清空共享文件列表");
    }

    public async Task StartAsync()
    {
        if (IsRunning) return;

        try
        {
            await EnsureUrlReservationAsync();

            await FirewallHelper.EnsureFirewallRuleAsync(_port, Log);

            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://+:{_port}/");
            _listener.Start();

            Log($"HTTP服务器已启动，端口: {_port}");
            OnServerStarted?.Invoke();

            _ = Task.Run(() => ListenLoop(_cts.Token));
        }
        catch (HttpListenerException ex) when (ex.ErrorCode == 5)
        {
            throw new UnauthorizedAccessException(
                "需要管理员权限来监听所有网络接口。请以管理员身份运行程序。\n" +
                $"或者手动执行: netsh http add urlacl url=http://+:{_port}/ user=Everyone", ex);
        }
        catch (Exception ex)
        {
            Log($"服务器启动失败: {ex.Message}");
            throw;
        }
    }

    public void Stop()
    {
        if (!IsRunning) return;

        _cts?.Cancel();
        _listener?.Stop();
        _listener?.Close();
        _listener = null;

        Log("HTTP服务器已停止");
        OnServerStopped?.Invoke();
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener?.IsListening == true)
        {
            try
            {
                var context = await _listener.GetContextAsync().WaitAsync(ct);
                _ = Task.Run(() => HandleRequestAsync(context), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log($"请求处理异常: {ex.Message}");
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        var remoteEndPoint = request.RemoteEndPoint?.ToString() ?? "未知";

        response.KeepAlive = false;
        response.Headers.Add("Connection", "close");
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

        if (request.HttpMethod == "OPTIONS")
        {
            response.StatusCode = 200;
            response.Close();
            return;
        }

        var path = request.Url?.AbsolutePath ?? "/";

        if (path == "/favicon.ico")
        {
            response.StatusCode = 204;
            response.Close();
            return;
        }

        try
        {
            switch (path)
            {
                case "/api/ping":
                    response.StatusCode = 200;
                    response.Close();
                    break;
                case "/api/status":
                    await HandleStatusAsync(response);
                    break;
                case "/api/files":
                    await HandleFileListAsync(response);
                    break;
                case "/api/upload":
                    await HandleUploadAsync(request, response, remoteEndPoint);
                    break;
                case var p when p.StartsWith("/api/download/"):
                    await HandleDownloadAsync(p, response, remoteEndPoint);
                    break;
                default:
                    await ServeStaticPageAsync(response);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log($"处理请求失败: {ex.Message}");
            try
            {
                if (response.OutputStream.CanWrite)
                {
                    response.StatusCode = 500;
                    var json = System.Text.Json.JsonSerializer.Serialize(new { error = ex.Message });
                    var buf = Encoding.UTF8.GetBytes(json);
                    response.ContentType = "application/json; charset=utf-8";
                    response.ContentLength64 = buf.Length;
                    await response.OutputStream.WriteAsync(buf);
                    await response.OutputStream.FlushAsync();
                }
            }
            catch { }
            finally
            {
                response.Close();
            }
        }
    }

    private async Task HandleStatusAsync(HttpListenerResponse response)
    {
        var localIp = GetLocalIPAddress();
        var status = new
        {
            serverName = Environment.MachineName,
            version = "1.0.0",
            port = _port,
            ip = localIp,
            sharedFileCount = _sharedFiles.Count,
            isRunning = IsRunning
        };
        await WriteJsonAsync(response, status);
    }

    private async Task HandleFileListAsync(HttpListenerResponse response)
    {
        var files = _sharedFiles.Values.Select(f => new
        {
            f.Id,
            f.FileName,
            f.FileSize,
            f.FileType,
            f.AddedTime,
            downloadUrl = $"/api/download/{f.Id}"
        });
        await WriteJsonAsync(response, new { files });
    }

    private async Task HandleUploadAsync(HttpListenerRequest request, HttpListenerResponse response, string remoteEndPoint)
    {
        var contentType = request.ContentType ?? "";
        if (!contentType.Contains("multipart/form-data"))
        {
            response.StatusCode = 400;
            await WriteJsonAsync(response, new { error = "需要 multipart/form-data 格式" });
            return;
        }

        var boundary = ExtractBoundary(contentType);
        if (boundary == null)
        {
            response.StatusCode = 400;
            await WriteJsonAsync(response, new { error = "无法解析 boundary" });
            return;
        }

        using var ms = new MemoryStream();
        await request.InputStream.CopyToAsync(ms);
        var body = ms.ToArray();

        var files = ParseMultipartData(body, boundary);
        var savedFiles = new List<object>();

        foreach (var (fileName, fileData) in files)
        {
            var safeName = SanitizeFileName(fileName);
            var savePath = Path.Combine(_downloadPath, safeName);

            savePath = EnsureUniqueFileName(savePath);
            await File.WriteAllBytesAsync(savePath, fileData);

            savedFiles.Add(new { fileName = safeName, size = fileData.Length, path = savePath });

            var log = new TransferLog
            {
                FileName = safeName,
                Direction = "接收",
                FileSize = fileData.Length,
                RemoteAddress = remoteEndPoint,
                Success = true,
                Message = "上传成功"
            };
            _transferLogs.Enqueue(log);
            OnTransferComplete?.Invoke(log);
            Log($"接收文件: {safeName} ({log.FileSizeDisplay}) 来自 {remoteEndPoint}");
        }

        await WriteJsonAsync(response, new { success = true, files = savedFiles });
    }

    private async Task HandleDownloadAsync(string path, HttpListenerResponse response, string remoteEndPoint)
    {
        var fileId = path.Replace("/api/download/", "").Trim();
        if (!_sharedFiles.TryGetValue(fileId, out var fileItem))
        {
            response.StatusCode = 404;
            await WriteJsonAsync(response, new { error = "文件不存在" });
            return;
        }

        if (!File.Exists(fileItem.FilePath))
        {
            response.StatusCode = 404;
            await WriteJsonAsync(response, new { error = "文件已被移动或删除" });
            return;
        }

        var fileBytes = await File.ReadAllBytesAsync(fileItem.FilePath);
        response.ContentType = "application/octet-stream";
        response.ContentLength64 = fileBytes.Length;
        response.Headers.Add("Content-Disposition",
            $"attachment; filename=\"{Uri.EscapeDataString(fileItem.FileName)}\"");

        try
        {
            await response.OutputStream.WriteAsync(fileBytes);
            await response.OutputStream.FlushAsync();
        }
        finally
        {
            response.Close();
        }

        var log = new TransferLog
        {
            FileName = fileItem.FileName,
            Direction = "发送",
            FileSize = fileBytes.Length,
            RemoteAddress = remoteEndPoint,
            Success = true,
            Message = "下载成功"
        };
        _transferLogs.Enqueue(log);
        OnTransferComplete?.Invoke(log);
        Log($"发送文件: {fileItem.FileName} ({log.FileSizeDisplay}) 到 {remoteEndPoint}");
    }

    private async Task ServeStaticPageAsync(HttpListenerResponse response)
    {
        try
        {
            var html = GenerateMobilePage();
            var buffer = Encoding.UTF8.GetBytes(html);
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer);
            await response.OutputStream.FlushAsync();
        }
        finally
        {
            response.Close();
        }
    }

    private string GenerateMobilePage()
    {
        var localIp = GetLocalIPAddress();
        var filesJson = JsonSerializer.Serialize(
            _sharedFiles.Values.Select(f => new
            {
                id = f.Id,
                fileName = f.FileName,
                fileSize = f.FileSize,
                fileType = f.FileType
            }));

        var machineName = System.Net.WebUtility.HtmlEncode(Environment.MachineName);
        var headerText = $"{machineName} ({localIp}:{_port})";

        var html = new StringBuilder();
        html.Append("<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"UTF-8\">");
        html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        html.Append("<title>局域网文件传输</title><style>");
        html.Append("*{margin:0;padding:0;box-sizing:border-box}");
        html.Append("body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;background:#f0f2f5;color:#333}");
        html.Append(".header{background:linear-gradient(135deg,#667eea 0%,#764ba2 100%);color:#fff;padding:20px;text-align:center}");
        html.Append(".header h1{font-size:1.5em;margin-bottom:5px}");
        html.Append(".header p{font-size:.85em;opacity:.9}");
        html.Append(".container{max-width:600px;margin:0 auto;padding:15px}");
        html.Append(".card{background:#fff;border-radius:12px;padding:20px;margin-bottom:15px;box-shadow:0 2px 8px rgba(0,0,0,.08)}");
        html.Append(".card h2{font-size:1.1em;margin-bottom:15px;color:#333}");
        html.Append(".file-list{list-style:none}");
        html.Append(".file-item{display:flex;align-items:center;padding:12px 0;border-bottom:1px solid #f0f0f0}");
        html.Append(".file-item:last-child{border-bottom:none}");
        html.Append(".file-icon{font-size:2em;margin-right:12px}");
        html.Append(".file-info{flex:1}");
        html.Append(".file-name{font-size:.95em;font-weight:500;word-break:break-all}");
        html.Append(".file-meta{font-size:.8em;color:#999;margin-top:3px}");
        html.Append(".download-btn{background:#667eea;color:#fff;border:none;padding:8px 16px;border-radius:20px;font-size:.85em;cursor:pointer;white-space:nowrap}");
        html.Append(".download-btn:hover{background:#5a6fd6}");
        html.Append(".upload-area{border:2px dashed #ddd;border-radius:12px;padding:30px;text-align:center;cursor:pointer;transition:all .3s}");
        html.Append(".upload-area:hover,.upload-area.dragover{border-color:#667eea;background:#f8f9ff}");
        html.Append(".upload-area p{color:#999;margin-top:10px}");
        html.Append(".upload-icon{font-size:3em}");
        html.Append("#fileInput{display:none}");
        html.Append(".progress{display:none;margin-top:15px}");
        html.Append(".progress-bar{height:6px;background:#e0e0e0;border-radius:3px;overflow:hidden}");
        html.Append(".progress-fill{height:100%;background:linear-gradient(90deg,#667eea,#764ba2);width:0%;transition:width .3s}");
        html.Append(".progress-text{font-size:.8em;color:#666;margin-top:5px;text-align:center}");
        html.Append(".empty{text-align:center;color:#ccc;padding:30px}");
        html.Append(".empty-icon{font-size:3em}");
        html.Append(".toast{position:fixed;top:20px;left:50%;transform:translateX(-50%);background:#333;color:#fff;padding:12px 24px;border-radius:25px;font-size:.9em;z-index:1000;opacity:0;transition:opacity .3s}");
        html.Append(".toast.show{opacity:1}");
        html.Append("</style></head><body>");
        html.Append("<div class=\"header\"><h1>\ud83d\udcc1 局域网文件传输</h1>");
        html.Append("<p>连接到 ").Append(headerText).Append("</p></div>");
        html.Append("<div class=\"container\"><div class=\"card\"><h2>\ud83d\udce4 上传文件到电脑</h2>");
        html.Append("<div class=\"upload-area\" id=\"uploadArea\" onclick=\"document.getElementById('fileInput').click()\">");
        html.Append("<div class=\"upload-icon\">\u2601\ufe0f</div><p>点击或拖拽文件到此处上传</p></div>");
        html.Append("<input type=\"file\" id=\"fileInput\" multiple onchange=\"uploadFiles(this.files)\"/>");
        html.Append("<div class=\"progress\" id=\"progress\"><div class=\"progress-bar\"><div class=\"progress-fill\" id=\"progressFill\"></div></div>");
        html.Append("<div class=\"progress-text\" id=\"progressText\">上传中...</div></div></div>");
        html.Append("<div class=\"card\"><h2>\ud83d\udce5 可下载文件</h2>");
        html.Append("<ul class=\"file-list\" id=\"fileList\"></ul>");
        html.Append("<div class=\"empty\" id=\"emptyState\"><div class=\"empty-icon\">\ud83d\udced</div><p>暂无共享文件</p></div></div></div>");
        html.Append("<div class=\"toast\" id=\"toast\"></div>");
        html.Append("<script>");
        html.Append("var files=").Append(filesJson).Append(";");
        html.Append("function renderFileList(){var list=document.getElementById('fileList');var empty=document.getElementById('emptyState');");
        html.Append("if(files.length===0){list.innerHTML='';empty.style.display='block';return;}empty.style.display='none';");
        html.Append("list.innerHTML=files.map(function(f){return '<li class=\"file-item\"><div class=\"file-icon\">'+getFileIcon(f.fileType)+'</div>");
        html.Append("<div class=\"file-info\"><div class=\"file-name\">'+f.fileName+'</div>");
        html.Append("<div class=\"file-meta\">'+formatSize(f.fileSize)+' · '+f.fileType+'</div></div>");
        html.Append("<button class=\"download-btn\" onclick=\"downloadFile(&#39;'+f.id+'&#39;,&#39;'+f.fileName+'&#39;)\">下载</button></li>';}).join('');}");
        html.Append("function getFileIcon(t){var i={'JPG':'🖼','JPEG':'🖼','PNG':'🖼','GIF':'🖼','MP4':'🎬','AVI':'🎬','MKV':'🎬','MP3':'🎵','WAV':'🎵','PDF':'📄','DOC':'📝','DOCX':'📝','ZIP':'📦','RAR':'📦','APK':'📱'};return i[t]||'📎';}");
        html.Append("function formatSize(b){var s=['B','KB','MB','GB'];var i=0;while(b>=1024&&i<s.length-1){b/=1024;i++;}return b.toFixed(1)+' '+s[i];}");
        html.Append("function downloadFile(id,name){var a=document.createElement('a');a.href='/api/download/'+id;a.download=name;a.click();showToast('开始下载: '+name);}");
        html.Append("async function uploadFiles(fileList){if(fileList.length===0)return;var p=document.getElementById('progress');");
        html.Append("var pf=document.getElementById('progressFill');var pt=document.getElementById('progressText');p.style.display='block';");
        html.Append("for(var i=0;i<fileList.length;i++){var file=fileList[i];var fd=new FormData();fd.append('file',file);");
        html.Append("try{var xhr=new XMLHttpRequest();await new Promise(function(resolve,reject){xhr.upload.onprogress=function(e){");
        html.Append("if(e.lengthComputable){var pct=Math.round((e.loaded/e.total)*100);pf.style.width=pct+'%';pt.textContent='上传 '+file.name+': '+pct+'%'}};");
        html.Append("xhr.onload=function(){if(xhr.status===200)resolve();else reject(new Error('上传失败'));};");
        html.Append("xhr.onerror=function(){reject(new Error('网络错误'));};xhr.open('POST','/api/upload');xhr.send(fd);});");
        html.Append("showToast('上传成功: '+file.name);}catch(e){showToast('上传失败: '+file.name);}}");
        html.Append("p.style.display='none';pf.style.width='0%';location.reload();}");
        html.Append("function showToast(msg){var t=document.getElementById('toast');t.textContent=msg;t.classList.add('show');setTimeout(function(){t.classList.remove('show')},2500);}");
        html.Append("var ua=document.getElementById('uploadArea');ua.addEventListener('dragover',function(e){e.preventDefault();ua.classList.add('dragover');});");
        html.Append("ua.addEventListener('dragleave',function(){ua.classList.remove('dragover');});");
        html.Append("ua.addEventListener('drop',function(e){e.preventDefault();ua.classList.remove('dragover');uploadFiles(e.dataTransfer.files);});");
        html.Append("renderFileList();</script></body></html>");

        return html.ToString();
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, object data)
    {
        try
        {
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
            var buffer = Encoding.UTF8.GetBytes(json);
            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer);
            await response.OutputStream.FlushAsync();
        }
        finally
        {
            response.Close();
        }
    }

    private static string? ExtractBoundary(string contentType)
    {
        var parts = contentType.Split(';');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("boundary=", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed["boundary=".Length..].Trim('"');
            }
        }
        return null;
    }

    private static List<(string fileName, byte[] data)> ParseMultipartData(byte[] body, string boundary)
    {
        var result = new List<(string, byte[])>();
        var boundaryBytes = Encoding.UTF8.GetBytes("--" + boundary);
        var endBoundaryBytes = Encoding.UTF8.GetBytes("--" + boundary + "--");
        var doubleCrlf = Encoding.UTF8.GetBytes("\r\n\r\n");

        var pos = 0;
        while (pos < body.Length)
        {
            var nextBoundary = IndexOf(body, boundaryBytes, pos);
            if (nextBoundary < 0) break;

            pos = nextBoundary + boundaryBytes.Length;

            if (pos + 2 <= body.Length && body[pos] == '\r' && body[pos + 1] == '\n')
                pos += 2;
            else if (pos < body.Length && body[pos] == '\n')
                pos += 1;

            if (pos + endBoundaryBytes.Length - boundaryBytes.Length <= body.Length)
            {
                if (IsMatch(body, pos, endBoundaryBytes))
                    break;
            }

            var headerEnd = IndexOf(body, doubleCrlf, pos);
            if (headerEnd < 0) break;

            var headerBytes = body[pos..headerEnd];
            var header = Encoding.UTF8.GetString(headerBytes);
            var fileName = ExtractFileName(header);
            if (fileName == null)
            {
                pos = headerEnd + doubleCrlf.Length;
                var nextB = IndexOf(body, boundaryBytes, pos);
                pos = nextB >= 0 ? nextB : body.Length;
                continue;
            }

            var dataStart = headerEnd + doubleCrlf.Length;
            var dataEnd = IndexOf(body, boundaryBytes, dataStart);
            if (dataEnd < 0) dataEnd = body.Length;

            if (dataEnd >= 2 && body[dataEnd - 2] == '\r' && body[dataEnd - 1] == '\n')
                dataEnd -= 2;
            else if (dataEnd >= 1 && body[dataEnd - 1] == '\n')
                dataEnd -= 1;

            var fileData = body[dataStart..dataEnd];
            result.Add((fileName, fileData));
            pos = dataEnd;
        }

        return result;
    }

    private static int IndexOf(byte[] source, byte[] pattern, int startIndex)
    {
        for (var i = startIndex; i <= source.Length - pattern.Length; i++)
        {
            if (IsMatch(source, i, pattern))
                return i;
        }
        return -1;
    }

    private static bool IsMatch(byte[] source, int start, byte[] pattern)
    {
        if (start + pattern.Length > source.Length) return false;
        for (var i = 0; i < pattern.Length; i++)
        {
            if (source[start + i] != pattern[i])
                return false;
        }
        return true;
    }

    private static string? ExtractFileName(string header)
    {
        var marker = "filename=\"";
        var idx = header.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            marker = "filename=";
            idx = header.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            var start = idx + marker.Length;
            var end = header.IndexOf(';', start);
            if (end < 0) end = header.IndexOf('\r', start);
            if (end < 0) end = header.Length;
            var name = header[start..end].Trim().Trim('"');
            return string.IsNullOrEmpty(name) ? null : name;
        }

        var startIdx = idx + marker.Length;
        var endIdx = header.IndexOf('"', startIdx);
        if (endIdx < 0) return null;
        return header[startIdx..endIdx];
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new StringBuilder(fileName);
        foreach (var c in invalid)
            sanitized.Replace(c, '_');
        return sanitized.Length > 0 ? sanitized.ToString() : "unnamed_file";
    }

    private static string EnsureUniqueFileName(string filePath)
    {
        if (!File.Exists(filePath)) return filePath;

        var dir = Path.GetDirectoryName(filePath)!;
        var name = Path.GetFileNameWithoutExtension(filePath);
        var ext = Path.GetExtension(filePath);
        var counter = 1;

        string newPath;
        do
        {
            newPath = Path.Combine(dir, $"{name}_{counter}{ext}");
            counter++;
        } while (File.Exists(newPath));

        return newPath;
    }

    public static string GetLocalIPAddress()
    {
        try
        {
            using var socket = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            var endPoint = socket.LocalEndPoint as IPEndPoint;
            return endPoint?.Address.ToString() ?? "127.0.0.1";
        }
        catch
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                var ip = host.AddressList.FirstOrDefault(
                    a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                return ip?.ToString() ?? "127.0.0.1";
            }
            catch
            {
                return "127.0.0.1";
            }
        }
    }

    private async Task EnsureUrlReservationAsync()
    {
        var url = $"http://+:{_port}/";

        if (await UrlAclExistsAsync(url))
            return;

        Log($"正在添加 URL ACL: {url}");

        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"http add urlacl url={url} user=Everyone",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            }
        };

        try
        {
            process.Start();
            await process.WaitForExitAsync();
            await Task.Delay(500);

            if (process.ExitCode != 0)
                throw new UnauthorizedAccessException(
                    $"URL ACL 添加失败 (退出码: {process.ExitCode})。\n" +
                    $"请以管理员身份运行，或手动执行:\nnetsh http add urlacl url={url} user=Everyone");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new UnauthorizedAccessException(
                $"需要URL保留权限。请以管理员身份运行，或手动执行:\n" +
                $"netsh http add urlacl url={url} user=Everyone");
        }
    }

    private static async Task<bool> UrlAclExistsAsync(string url)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = "http show urlacl",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };

            var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return true;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            return process.ExitCode == 0 && output.Contains(url);
        }
        catch
        {
            return true;
        }
    }

    private void Log(string message)
    {
        OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");
    }
}