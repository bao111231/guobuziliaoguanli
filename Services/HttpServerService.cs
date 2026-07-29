using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GuoBuZiLiaoGuanLi.Models;

namespace GuoBuZiLiaoGuanLi.Services;

public class HttpServerService
{
    private TcpListener _tcpListener;
    private Thread _listenerThread;
    private bool _isRunning;
    private int _port;
    private string _rootDirectory;
    private string _localIp;

    public event Action OnFoldersChanged;

    public string ServerUrl => $"http://{_localIp}:{_port}/";
    public bool IsRunning => _isRunning;
    public int Port => _port;
    public string LocalIp => _localIp;
    public List<string> DiagnosticMessages { get; } = new();
    public List<string> PendingConfirmations { get; } = new();

    public HttpServerService()
    {
    }

    public bool TryStart(string rootDirectory, int preferredPort = 8888)
    {
        if (_isRunning) return true;

        _rootDirectory = rootDirectory;
        DiagnosticMessages.Clear();
        PendingConfirmations.Clear();

        _localIp = GetLocalIPAddress();
        DiagnosticMessages.Add($"检测到本机局域网IP: {_localIp}");

        int[] portsToTry = { preferredPort, 8080, 8888, 9000, 9090, 18080, 28080, 38080 };

        foreach (int port in portsToTry)
        {
            if (TryStartOnPort(port))
            {
                DiagnosticMessages.Add($"成功绑定端口: {port} (使用TcpListener, 无需管理员权限)");

                AddFirewallRule(port);
                return true;
            }
            else
            {
                DiagnosticMessages.Add($"端口 {port} 绑定失败，尝试下一个...");
            }
        }

        return false;
    }

    private void AddFirewallRule(int port)
    {
        try
        {
            string ruleName = $"GuoBuZiLiaoGuanLi_Port_{port}";

            // 检查规则是否已存在（不需要管理员）
            string checkArgs = $"/c netsh advfirewall firewall show rule name=\"{ruleName}\" >nul 2>&1";
            var checkProc = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = checkArgs,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false
            });
            checkProc.WaitForExit();

            if (checkProc.ExitCode == 0)
            {
                DiagnosticMessages.Add($"防火墙规则已存在: 允许TCP {port} 入站");
                return;
            }

            // 尝试添加规则（需要管理员权限，非管理员会失败但不影响服务启动）
            string addArgs = $"/c netsh advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow protocol=TCP localport={port}";
            var addProc = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = addArgs,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false
            });
            addProc.WaitForExit();

            if (addProc.ExitCode == 0)
            {
                DiagnosticMessages.Add($"防火墙规则已添加: 允许TCP {port} 入站");
            }
            else
            {
                DiagnosticMessages.Add("提示: 未能自动添加防火墙规则(需要管理员权限)。首次访问时Windows会弹出防火墙提示，请点击「允许访问」");
            }
        }
        catch
        {
            DiagnosticMessages.Add("提示: 防火墙规则检查失败，首次访问时请允许Windows防火墙提示");
        }
    }

    private bool TryStartOnPort(int port)
    {
        try
        {
            _tcpListener = new TcpListener(IPAddress.Any, port);
            _tcpListener.Start();
            _isRunning = true;
            _port = port;

            _listenerThread = new Thread(ListenLoop)
            {
                IsBackground = true
            };
            _listenerThread.Start();

            return true;
        }
        catch (Exception ex)
        {
            DiagnosticMessages.Add($"  绑定异常: {ex.Message}");
            try { _tcpListener?.Stop(); } catch { }
            _tcpListener = null;
            return false;
        }
    }

    public void Stop()
    {
        _isRunning = false;
        try
        {
            _tcpListener?.Stop();
        }
        catch { }
    }

    private void ListenLoop()
    {
        while (_isRunning)
        {
            try
            {
                var client = _tcpListener.AcceptTcpClient();
                _ = Task.Run(() => HandleClient(client));
            }
            catch
            {
                if (_isRunning)
                {
                    Thread.Sleep(100);
                }
            }
        }
    }

    private void HandleClient(TcpClient client)
    {
        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                client.ReceiveTimeout = 15000;
                client.SendTimeout = 60000;

                // 读取请求行
                string requestLine = ReadLine(stream);
                if (string.IsNullOrEmpty(requestLine))
                {
                    return;
                }

                var parts = requestLine.Split(' ');
                if (parts.Length < 3)
                {
                    return;
                }

                string method = parts[0];
                string rawUrl = parts[1];

                // 读取请求头
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                while (true)
                {
                    string headerLine = ReadLine(stream);
                    if (string.IsNullOrEmpty(headerLine))
                    {
                        break;
                    }
                    int idx = headerLine.IndexOf(':');
                    if (idx > 0)
                    {
                        string key = headerLine.Substring(0, idx).Trim();
                        string value = headerLine.Substring(idx + 1).Trim();
                        headers[key] = value;
                    }
                }

                // 读取请求体
                byte[] body = Array.Empty<byte>();
                if (headers.TryGetValue("Content-Length", out string contentLengthStr) &&
                    int.TryParse(contentLengthStr, out int contentLength) &&
                    contentLength > 0)
                {
                    body = new byte[contentLength];
                    int totalRead = 0;
                    while (totalRead < contentLength)
                    {
                        int read = stream.Read(body, totalRead, contentLength - totalRead);
                        if (read == 0) break;
                        totalRead += read;
                    }
                }

                // 解析路径
                string path = rawUrl.Split('?')[0];

                // 处理请求
                HandleRequest(method, path, body, stream);
            }
        }
        catch
        {
            // 忽略连接错误
        }
    }

    private static string ReadLine(NetworkStream stream)
    {
        var sb = new StringBuilder();
        int b;
        while ((b = stream.ReadByte()) != -1)
        {
            if (b == '\r')
            {
                int next = stream.ReadByte();
                if (next == '\n')
                {
                    break;
                }
                sb.Append((char)b);
                if (next != -1 && next != '\r') sb.Append((char)next);
            }
            else if (b == '\n')
            {
                break;
            }
            else
            {
                sb.Append((char)b);
            }
        }
        return sb.ToString();
    }

    private void HandleRequest(string method, string path, byte[] body, NetworkStream stream)
    {
        var responseHeaders = new Dictionary<string, string>
        {
            ["Access-Control-Allow-Origin"] = "*",
            ["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS",
            ["Access-Control-Allow-Headers"] = "Content-Type"
        };

        try
        {
            if (method == "OPTIONS")
            {
                WriteTextResponse(stream, 200, "OK", "", "text/plain", responseHeaders);
                return;
            }

            if (path == "/" || path == "")
            {
                string html = GetIndexHtml();
                WriteTextResponse(stream, 200, "OK", html, "text/html; charset=utf-8", responseHeaders);
            }
            else if (path == "/api/folders" && method == "GET")
            {
                string json = GetFoldersJson();
                WriteTextResponse(stream, 200, "OK", json, "application/json; charset=utf-8", responseHeaders);
            }
            else if (path == "/api/mark" && method == "POST")
            {
                HandleMarkUploaded(body, stream, responseHeaders);
            }
            else if (path == "/api/test")
            {
                WriteTextResponse(stream, 200, "OK", "ok", "text/plain", responseHeaders);
            }
            else if (path.StartsWith("/d/"))
            {
                string encodedPath = path.Substring(3);
                HandleDownload(encodedPath, stream, responseHeaders);
            }
            else
            {
                WriteTextResponse(stream, 404, "Not Found", "Not Found", "text/plain", responseHeaders);
            }
        }
        catch (Exception ex)
        {
            try
            {
                WriteTextResponse(stream, 500, "Internal Server Error", $"Error: {ex.Message}", "text/plain", new Dictionary<string, string>());
            }
            catch { }
        }
    }

    private void WriteTextResponse(NetworkStream stream, int statusCode, string statusText,
        string content, string contentType, Dictionary<string, string> headers)
    {
        byte[] body = Encoding.UTF8.GetBytes(content);
        WriteBinaryResponse(stream, statusCode, statusText, body, contentType, headers);
    }

    private void WriteBinaryResponse(NetworkStream stream, int statusCode, string statusText,
        byte[] body, string contentType, Dictionary<string, string> headers)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {statusCode} {statusText}\r\n");
        sb.Append($"Content-Type: {contentType}\r\n");
        sb.Append($"Content-Length: {body.Length}\r\n");
        sb.Append("Connection: close\r\n");
        foreach (var header in headers)
        {
            sb.Append($"{header.Key}: {header.Value}\r\n");
        }
        sb.Append("\r\n");

        byte[] headerBytes = Encoding.UTF8.GetBytes(sb.ToString());
        stream.Write(headerBytes, 0, headerBytes.Length);
        stream.Write(body, 0, body.Length);
        stream.Flush();
    }

    private string GetFoldersJson()
    {
        var folders = new List<object>();

        try
        {
            if (Directory.Exists(_rootDirectory))
            {
                var directories = Directory.GetDirectories(_rootDirectory);
                foreach (var dir in directories)
                {
                    if (string.Equals(Path.GetFileName(dir), "暂存文件", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var folder = new CustomerFolder(dir);

                    bool isPending = folder.Status == FolderStatus.NotUploaded ||
                                     folder.Status == FolderStatus.UploadedUnconfirmed;

                    if (isPending)
                    {
                        var images = new List<object>();
                        foreach (var doc in folder.Documents.Values)
                        {
                            foreach (var file in doc.AllFiles)
                            {
                                string encoded = MakeUrlSafe(Convert.ToBase64String(Encoding.UTF8.GetBytes(file)));
                                images.Add(new
                                {
                                    name = Path.GetFileName(file),
                                    docType = doc.DisplayName,
                                    url = $"/d/{encoded}"
                                });
                            }
                        }

                        string folderPathEncoded = MakeUrlSafe(Convert.ToBase64String(Encoding.UTF8.GetBytes(folder.FolderPath)));

                        folders.Add(new
                        {
                            name = folder.DisplayName,
                            originalName = folder.OriginalFolderName,
                            creationTime = folder.CreationTime.ToString("yyyy-MM-dd HH:mm"),
                            imageCount = images.Count,
                            images = images,
                            pathEncoded = folderPathEncoded,
                            isMarked = folder.Status == FolderStatus.UploadedUnconfirmed
                        });
                    }
                }
            }
        }
        catch { }

        return JsonSerializer.Serialize(new { folders }, new JsonSerializerOptions { WriteIndented = false });
    }

    private void HandleMarkUploaded(byte[] body, NetworkStream stream, Dictionary<string, string> responseHeaders)
    {
        try
        {
            string bodyText = Encoding.UTF8.GetString(body);

            using var doc = JsonDocument.Parse(bodyText);
            string encodedPath = doc.RootElement.GetProperty("path").GetString();
            string base64 = DecodeUrlSafe(encodedPath);
            string folderPath = Encoding.UTF8.GetString(Convert.FromBase64String(base64));

            if (!Directory.Exists(folderPath))
            {
                WriteTextResponse(stream, 404, "Not Found", "Folder not found", "text/plain", responseHeaders);
                return;
            }

            string fullPath = Path.GetFullPath(folderPath);
            string rootFullPath = Path.GetFullPath(_rootDirectory);
            if (!fullPath.StartsWith(rootFullPath, StringComparison.OrdinalIgnoreCase))
            {
                WriteTextResponse(stream, 403, "Forbidden", "Forbidden", "text/plain", responseHeaders);
                return;
            }

            var folder = new CustomerFolder(folderPath);

            const string uploadedSuffix = "已上传";
            string folderName = Path.GetFileName(folderPath);

            if (!folderName.EndsWith(uploadedSuffix))
            {
                string parentDir = Path.GetDirectoryName(folderPath);
                string newFolderName = folder.OriginalFolderName + uploadedSuffix;
                string newFolderPath = Path.Combine(parentDir, newFolderName);

                if (!Directory.Exists(newFolderPath))
                {
                    Directory.Move(folderPath, newFolderPath);

                    if (!PendingConfirmations.Contains(folder.OriginalFolderName))
                    {
                        PendingConfirmations.Add(folder.OriginalFolderName);
                    }
                }
            }

            OnFoldersChanged?.Invoke();

            WriteTextResponse(stream, 200, "OK", "{\"success\":true}", "application/json", responseHeaders);
        }
        catch (Exception ex)
        {
            WriteTextResponse(stream, 500, "Internal Server Error", $"Error: {ex.Message}", "text/plain", responseHeaders);
        }
    }

    private void HandleDownload(string encodedPath, NetworkStream stream, Dictionary<string, string> responseHeaders)
    {
        try
        {
            string base64 = DecodeUrlSafe(encodedPath);
            string filePath = Encoding.UTF8.GetString(Convert.FromBase64String(base64));

            if (!File.Exists(filePath))
            {
                WriteTextResponse(stream, 404, "Not Found", "File not found", "text/plain", responseHeaders);
                return;
            }

            string fullPath = Path.GetFullPath(filePath);
            string rootFullPath = Path.GetFullPath(_rootDirectory);
            if (!fullPath.StartsWith(rootFullPath, StringComparison.OrdinalIgnoreCase))
            {
                WriteTextResponse(stream, 403, "Forbidden", "Forbidden", "text/plain", responseHeaders);
                return;
            }

            string fileName = Path.GetFileName(filePath);
            string fileExt = Path.GetExtension(filePath).ToLower();
            string contentType = GetContentType(fileExt);

            var fileInfo = new FileInfo(filePath);
            var headers = new Dictionary<string, string>(responseHeaders)
            {
                ["Content-Disposition"] = $"attachment; filename*=UTF-8''{Uri.EscapeDataString(fileName)}"
            };

            // 写入响应头
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 200 OK\r\n");
            sb.Append($"Content-Type: {contentType}\r\n");
            sb.Append($"Content-Length: {fileInfo.Length}\r\n");
            sb.Append("Connection: close\r\n");
            foreach (var header in headers)
            {
                sb.Append($"{header.Key}: {header.Value}\r\n");
            }
            sb.Append("\r\n");

            byte[] headerBytes = Encoding.UTF8.GetBytes(sb.ToString());
            stream.Write(headerBytes, 0, headerBytes.Length);

            // 流式传输文件
            using (var fileStream = File.OpenRead(filePath))
            {
                fileStream.CopyTo(stream);
            }
            stream.Flush();
        }
        catch (Exception ex)
        {
            try
            {
                WriteTextResponse(stream, 500, "Internal Server Error", $"Download error: {ex.Message}", "text/plain", responseHeaders);
            }
            catch { }
        }
    }

    private static string MakeUrlSafe(string base64)
    {
        return base64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string DecodeUrlSafe(string urlSafe)
    {
        string base64 = urlSafe.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        return base64;
    }

    private string GetContentType(string ext)
    {
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".tiff" or ".tif" => "image/tiff",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream"
        };
    }

    public static string GetLocalIPAddress()
    {
        try
        {
            var validIPs = new List<(string ip, int score)>();

            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                var props = ni.GetIPProperties();
                foreach (var addr in props.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        string ip = addr.Address.ToString();
                        if (ip.StartsWith("169.254.")) continue;

                        int score = 0;
                        if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) score += 100;
                        if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet) score += 50;
                        if (ip.StartsWith("192.168.")) score += 20;
                        else if (ip.StartsWith("10.")) score += 10;
                        else if (ip.StartsWith("172.")) score += 5;

                        validIPs.Add((ip, score));
                    }
                }
            }

            if (validIPs.Count > 0)
            {
                return validIPs.OrderByDescending(x => x.score).First().ip;
            }
        }
        catch { }

        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is IPEndPoint endPoint)
            {
                string ip = endPoint.Address.ToString();
                if (!ip.StartsWith("169.254.") && !ip.StartsWith("127."))
                {
                    return ip;
                }
            }
        }
        catch { }

        return "127.0.0.1";
    }

    private string GetIndexHtml()
    {
        return @"<!DOCTYPE html>
<html lang=""zh-CN"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>国补资料下载</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', sans-serif; background: #f5f7fa; padding: 16px; color: #2d3748; }
        .header { background: white; border-radius: 12px; padding: 20px; margin-bottom: 16px; box-shadow: 0 1px 3px rgba(0,0,0,0.1); }
        .header h1 { font-size: 22px; margin-bottom: 8px; color: #007bff; }
        .header p { color: #718096; font-size: 14px; }
        .folder-card { background: white; border-radius: 12px; padding: 16px; margin-bottom: 12px; box-shadow: 0 1px 3px rgba(0,0,0,0.1); border-left: 4px solid #007bff; }
        .folder-card.marked { border-left-color: #ffc107; opacity: 0.75; }
        .folder-header { display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 12px; flex-wrap: wrap; gap: 10px; }
        .folder-name { font-size: 17px; font-weight: 600; word-break: break-all; }
        .marked-badge { display: inline-block; background: #fff3cd; color: #856404; font-size: 12px; padding: 2px 8px; border-radius: 4px; margin-left: 8px; font-weight: normal; }
        .folder-info { font-size: 13px; color: #718096; margin-top: 4px; }
        .btn-group { display: flex; gap: 8px; flex-wrap: wrap; }
        .download-btn { background: #007bff; color: white; border: none; padding: 10px 16px; border-radius: 8px; font-size: 14px; cursor: pointer; font-weight: 500; white-space: nowrap; }
        .download-btn:active { background: #0056b3; }
        .download-btn:disabled { background: #a0aec0; cursor: not-allowed; }
        .mark-btn { background: #ffc107; color: #212529; border: none; padding: 10px 16px; border-radius: 8px; font-size: 14px; cursor: pointer; font-weight: 500; white-space: nowrap; }
        .mark-btn:active { background: #e0a800; }
        .mark-btn:disabled { background: #e9ecef; color: #6c757d; cursor: not-allowed; }
        .mark-btn.marked { background: #28a745; color: white; }
        .image-list { margin-top: 12px; }
        .image-item { display: flex; align-items: center; padding: 8px 12px; background: #f7fafc; border-radius: 6px; margin-bottom: 6px; font-size: 14px; word-break: break-all; }
        .image-item .doc-type { color: #718096; font-size: 12px; margin-right: 8px; white-space: nowrap; }
        .progress { margin-top: 8px; display: none; }
        .progress-bar { height: 4px; background: #e2e8f0; border-radius: 2px; overflow: hidden; }
        .progress-fill { height: 100%; background: #007bff; width: 0%; transition: width 0.3s; }
        .progress-text { font-size: 12px; color: #718096; margin-top: 4px; }
        .empty { text-align: center; padding: 60px 20px; color: #a0aec0; }
        .empty-icon { font-size: 48px; margin-bottom: 16px; }
        .summary { background: #ebf5ff; padding: 12px 16px; border-radius: 8px; margin-bottom: 16px; font-size: 14px; color: #0056b3; }
        .tip { background: #fff3cd; padding: 10px 14px; border-radius: 8px; margin-bottom: 16px; font-size: 13px; color: #856404; }
        .marked-section { margin-top: 24px; padding-top: 16px; border-top: 2px dashed #dee2e6; }
        .section-title { font-size: 15px; color: #856404; font-weight: 600; margin-bottom: 12px; }
        .search-box { background: white; border-radius: 10px; padding: 10px 14px; margin-bottom: 12px; box-shadow: 0 1px 3px rgba(0,0,0,0.1); display: flex; align-items: center; gap: 10px; }
        .search-icon { font-size: 18px; }
        .search-input { flex: 1; border: none; font-size: 15px; outline: none; background: transparent; color: #2d3748; }
        .search-input::placeholder { color: #a0aec0; }
        .clear-btn { background: #e2e8f0; border: none; width: 24px; height: 24px; border-radius: 50%; font-size: 14px; color: #718096; cursor: pointer; display: none; align-items: center; justify-content: center; padding: 0; line-height: 1; }
        .clear-btn.visible { display: flex; }
        .no-results { text-align: center; padding: 40px 20px; color: #a0aec0; }
    </style>
</head>
<body>
    <div class=""header"">
        <h1>📱 国补资料下载</h1>
        <p>请确保手机和电脑连接在同一WiFi网络下，下载图片后点击「标记已上传」</p>
    </div>
    <div class=""tip"">💡 标记后需要在电脑端确认才算正式上传完成</div>
    <div class=""search-box"">
        <span class=""search-icon"">🔍</span>
        <input type=""text"" class=""search-input"" id=""searchInput"" placeholder=""搜索客户姓名..."">
        <button class=""clear-btn"" id=""clearBtn"">✕</button>
    </div>
    <div id=""summary""></div>
    <div id=""folderList""><div class=""empty""><div class=""empty-icon"">⏳</div><p>加载中...</p></div></div>

    <script>
        let foldersData = [];
        let isDownloading = false;
        let searchText = '';

        async function loadFolders() {
            try {
                const resp = await fetch('/api/folders');
                const data = await resp.json();
                foldersData = data.folders;
                renderFolders();
            } catch (e) {
                document.getElementById('folderList').innerHTML = '<div class=""empty""><div class=""empty-icon"">⚠️</div><p>加载失败，请刷新页面重试</p></div>';
            }
        }

        function getFilteredFolders() {
            if (!searchText) {
                return foldersData;
            }
            const search = searchText.toLowerCase();
            return foldersData.filter(f =>
                f.name.toLowerCase().includes(search) ||
                f.originalName.toLowerCase().includes(search)
            );
        }

        function renderFolders() {
            const listEl = document.getElementById('folderList');
            const summaryEl = document.getElementById('summary');
            const clearBtn = document.getElementById('clearBtn');

            const filteredFolders = getFilteredFolders();
            const unmarked = filteredFolders.filter(f => !f.isMarked);
            const marked = filteredFolders.filter(f => f.isMarked);

            clearBtn.className = 'clear-btn' + (searchText ? ' visible' : '');

            if (foldersData.length === 0) {
                summaryEl.innerHTML = '';
                listEl.innerHTML = '<div class=""empty""><div class=""empty-icon"">✅</div><p>暂无可处理的资料</p></div>';
                return;
            }

            const totalImages = foldersData.reduce((s, f) => s + f.imageCount, 0);
            let summaryText = '共 ' + foldersData.length + ' 个文件夹，合计 ' + totalImages + ' 张图片';

            if (searchText) {
                const filteredImages = filteredFolders.reduce((s, f) => s + f.imageCount, 0);
                summaryText = '搜索 「' + escapeHtml(searchText) + '」：找到 ' + filteredFolders.length + ' 个文件夹，' + filteredImages + ' 张图片';
            }

            if (marked.length > 0 && !searchText) {
                summaryText += '，已标记 ' + marked.length + ' 个待电脑确认';
            }
            summaryEl.innerHTML = '<div class=""summary"">' + summaryText + '</div>';

            if (filteredFolders.length === 0 && searchText) {
                listEl.innerHTML = '<div class=""no-results""><div class=""empty-icon"">🔍</div><p>未找到匹配的客户</p></div>';
                return;
            }

            let html = '';

            if (unmarked.length > 0) {
                unmarked.forEach(function(folder) {
                    const globalIdx = foldersData.indexOf(folder);
                    html += renderFolderCard(folder, false, globalIdx);
                });
            }

            if (marked.length > 0) {
                html += '<div class=""marked-section"">';
                html += '  <div class=""section-title"">⏳ 已标记，等待电脑端确认 (' + marked.length + ')</div>';
                marked.forEach(function(folder) {
                    const globalIdx = foldersData.indexOf(folder);
                    html += renderFolderCard(folder, true, globalIdx);
                });
                html += '</div>';
            }

            listEl.innerHTML = html;
        }

        function renderFolderCard(folder, isMarked, idx) {
            let cardHtml = '<div class=""folder-card' + (isMarked ? ' marked' : '') + '"" id=""folder-' + idx + '"">';
            cardHtml += '  <div class=""folder-header"">';
            cardHtml += '    <div style=""flex:1;min-width:0;"">';
            cardHtml += '      <div class=""folder-name"">' + escapeHtml(folder.name);
            if (isMarked) {
                cardHtml += '<span class=""marked-badge"">⏳ 已标记待确认</span>';
            }
            cardHtml += '</div>';
            cardHtml += '      <div class=""folder-info"">创建时间: ' + folder.creationTime + ' · ' + folder.imageCount + ' 张图片</div>';
            cardHtml += '    </div>';
            cardHtml += '    <div class=""btn-group"">';
            cardHtml += '      <button class=""download-btn"" onclick=""downloadFolder(' + idx + ')"" id=""btn-dl-' + idx + '"">' + (isMarked ? '📥 重新下载' : '📥 下载全部') + '</button>';
            if (!isMarked) {
                cardHtml += '      <button class=""mark-btn"" onclick=""markUploaded(' + idx + ')"" id=""btn-mk-' + idx + '"">✅ 标记已上传</button>';
            } else {
                cardHtml += '      <button class=""mark-btn marked"" disabled>✅ 已标记</button>';
            }
            cardHtml += '    </div>';
            cardHtml += '  </div>';

            if (!isMarked) {
                cardHtml += '  <div class=""image-list"">';
                folder.images.forEach(function(img) {
                    cardHtml += '    <div class=""image-item""><span class=""doc-type"">[' + escapeHtml(img.docType) + ']</span>' + escapeHtml(img.name) + '</div>';
                });
                cardHtml += '  </div>';
            }

            cardHtml += '  <div class=""progress"" id=""progress-' + idx + '"">';
            cardHtml += '    <div class=""progress-bar""><div class=""progress-fill"" id=""fill-' + idx + '""></div></div>';
            cardHtml += '    <div class=""progress-text"" id=""text-' + idx + '"">准备下载...</div>';
            cardHtml += '  </div>';
            cardHtml += '</div>';
            return cardHtml;
        }

        async function markUploaded(idx) {
            const folder = foldersData[idx];
            if (!folder) return;
            const btn = document.getElementById('btn-mk-' + idx);
            if (!btn) return;

            btn.disabled = true;
            btn.textContent = '标记中...';

            try {
                const resp = await fetch('/api/mark', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ path: folder.pathEncoded })
                });

                if (resp.ok) {
                    folder.isMarked = true;
                    renderFolders();
                    window.scrollTo({ top: document.body.scrollHeight, behavior: 'smooth' });
                } else {
                    alert('标记失败，请重试');
                    btn.disabled = false;
                    btn.textContent = '✅ 标记已上传';
                }
            } catch (e) {
                alert('标记失败: ' + e.message);
                btn.disabled = false;
                btn.textContent = '✅ 标记已上传';
            }
        }

        async function downloadFolder(idx) {
            if (isDownloading) return;

            const folder = foldersData[idx];
            if (!folder) return;

            if (folder.isMarked) {
                if (!confirm('该文件夹已标记为已上传，是否需要重新下载图片？')) {
                    return;
                }
            }

            isDownloading = true;

            const btn = document.getElementById('btn-dl-' + idx);
            const progressEl = document.getElementById('progress-' + idx);
            const fillEl = document.getElementById('fill-' + idx);
            const textEl = document.getElementById('text-' + idx);

            if (!btn || !progressEl) {
                isDownloading = false;
                return;
            }

            btn.disabled = true;
            btn.textContent = '下载中...';
            progressEl.style.display = 'block';

            for (let i = 0; i < folder.images.length; i++) {
                const img = folder.images[i];
                const percent = Math.round((i / folder.images.length) * 100);
                fillEl.style.width = percent + '%';
                textEl.textContent = '正在下载 (' + (i + 1) + '/' + folder.images.length + '): ' + img.name;

                try {
                    const a = document.createElement('a');
                    a.href = img.url;
                    a.download = img.name;
                    a.target = '_blank';
                    document.body.appendChild(a);
                    a.click();
                    document.body.removeChild(a);
                    await sleep(800);
                } catch (e) {
                    console.error('Download error:', e);
                }
            }

            fillEl.style.width = '100%';
            if (folder.isMarked) {
                textEl.textContent = '重新下载完成！共 ' + folder.images.length + ' 张图片';
            } else {
                textEl.textContent = '下载完成！共 ' + folder.images.length + ' 张图片，请确认保存到手机后点击标记';
            }
            btn.textContent = '📥 重新下载';
            btn.disabled = false;

            isDownloading = false;
        }

        function sleep(ms) {
            return new Promise(function(resolve) { setTimeout(resolve, ms); });
        }

        function escapeHtml(str) {
            const div = document.createElement('div');
            div.textContent = str;
            return div.innerHTML;
        }

        const searchInput = document.getElementById('searchInput');
        const clearBtn = document.getElementById('clearBtn');

        searchInput.addEventListener('input', function(e) {
            searchText = e.target.value;
            renderFolders();
        });

        clearBtn.addEventListener('click', function() {
            searchText = '';
            searchInput.value = '';
            searchInput.focus();
            renderFolders();
        });

        loadFolders();

        setInterval(loadFolders, 5000);
    </script>
</body>
</html>";
    }
}
