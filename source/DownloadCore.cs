using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace EasyDownload
{
    public sealed class DownloadProgress
    {
        public string Name;
        public long Total, Completed, Speed;
        public int Connections;
    }

    public sealed class DownloadResult
    {
        public string Path;
        public long Bytes;
    }

    // A separate local aria2 process owns one download. Downloads never write over a user's file.
    public sealed class DownloadCore
    {
        readonly string enginePath;
        public DownloadCore(string enginePath) { this.enginePath = enginePath; }

        public static Uri ValidateUrl(string input)
        {
            Uri uri;
            if (String.IsNullOrWhiteSpace(input) || input.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0 ||
                !Uri.TryCreate(input.Trim(), UriKind.Absolute, out uri) ||
                (uri.Scheme != "https" && uri.Scheme != "http") || String.IsNullOrEmpty(uri.Host))
                throw new ArgumentException("请粘贴以 https:// 或 http:// 开头的文件下载链接。");
            if (!String.IsNullOrEmpty(uri.UserInfo))
                throw new ArgumentException("暂不支持在链接中填写账号密码，请使用公开下载直链。");
            return new UriBuilder(uri) { Fragment = "" }.Uri;
        }

        public static void ValidateName(string name)
        {
            if (String.IsNullOrEmpty(name) || name == "." || name == ".." || name.Length > 180 ||
                name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith(".") || name.EndsWith(" "))
                throw new ArgumentException("文件名不能包含 \\ / : * ? \" < > |，不能以点或空格结尾，最长 180 个字符。");
            string stem = name.Split('.')[0].TrimEnd().ToUpperInvariant();
            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" ||
                System.Text.RegularExpressions.Regex.IsMatch(stem, @"^(COM|LPT)[0-9¹²³]$"))
                throw new ArgumentException("这个名称是 Windows 保留名称，请换一个文件名。");
        }

        static string SafeRemoteName(string input)
        {
            string name = input.Replace('\\', '/');
            name = name.Substring(name.LastIndexOf('/') + 1);
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Trim().TrimEnd('.', ' ');
            if (String.IsNullOrWhiteSpace(name)) name = "下载文件";
            if (name.Length > 180)
            {
                string ext = Path.GetExtension(name);
                if (ext.Length > 24) ext = "";
                name = name.Substring(0, 180 - ext.Length) + ext;
            }
            try { ValidateName(name); }
            catch (ArgumentException) { name = "下载_" + name; }
            return name;
        }

        static string QueryName(Uri uri)
        {
            foreach (string item in uri.Query.TrimStart('?').Split('&'))
            {
                int equal = item.IndexOf('=');
                if (equal < 0) continue;
                string key = Uri.UnescapeDataString(item.Substring(0, equal)).ToLowerInvariant();
                if (key != "filename" && key != "file_name" && key != "name") continue;
                string value = Uri.UnescapeDataString(item.Substring(equal + 1).Replace("+", " "));
                if (!String.IsNullOrWhiteSpace(value) && !String.IsNullOrEmpty(Path.GetExtension(value)))
                    return SafeRemoteName(value);
            }
            return null;
        }

        static string Publish(string source, string folder, string name)
        {
            ValidateName(name);
            string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
            for (int i = 0; i < 10000; i++)
            {
                string target = Path.Combine(folder, i == 0 ? name : stem + " (" + i + ")" + ext);
                if (File.Exists(target) || Directory.Exists(target)) continue;
                try { File.Move(source, target); return target; }
                catch (IOException) { if (File.Exists(target) || Directory.Exists(target)) continue; throw; }
            }
            throw new IOException("保存目录中重名文件过多，请换一个文件名。");
        }

        public async Task<DownloadResult> DownloadAsync(string url, string folder, string manualName,
            IProgress<DownloadProgress> progress, CancellationToken token)
        {
            Uri uri = ValidateUrl(url);
            if (!String.IsNullOrEmpty(manualName)) ValidateName(manualName);
            if (!File.Exists(enginePath)) throw new FileNotFoundException("下载引擎缺失，请保留应用目录中的 engine 文件夹。");
            folder = Path.GetFullPath(folder);
            Directory.CreateDirectory(folder);
            string staging = Path.Combine(folder, ".轻松下载-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            File.SetAttributes(staging, File.GetAttributes(staging) | FileAttributes.Hidden);
            AriaSession session = null;
            string published = null;
            try
            {
                token.ThrowIfCancellationRequested();
                session = new AriaSession(enginePath);
                await session.StartAsync(token).ConfigureAwait(false);
                var options = new Dictionary<string, object> {
                    {"dir", staging}, {"split", "8"}, {"max-connection-per-server", "8"},
                    {"min-split-size", "1M"}, {"allow-overwrite", "false"}, {"auto-file-renaming", "false"},
                    {"file-allocation", "none"}, {"max-tries", "4"}, {"retry-wait", "2"},
                    {"connect-timeout", "15"}, {"timeout", "30"}, {"max-file-not-found", "2"},
                    {"http-accept-gzip", "false"}, {"check-certificate", "true"},
                    {"follow-torrent", "false"}, {"follow-metalink", "false"}, {"max-download-limit", "0"}
                };
                if (!String.IsNullOrEmpty(manualName)) options["out"] = manualName;
                // Honor an existing Windows proxy. Loopback/RPC traffic always bypasses proxies.
                if (!uri.IsLoopback && String.IsNullOrEmpty(Environment.GetEnvironmentVariable("https_proxy")) &&
                    String.IsNullOrEmpty(Environment.GetEnvironmentVariable("http_proxy")) &&
                    String.IsNullOrEmpty(Environment.GetEnvironmentVariable("all_proxy")))
                {
                    try
                    {
                        IWebProxy proxy = WebRequest.GetSystemWebProxy();
                        Uri address = proxy.GetProxy(uri);
                        if (address != null && address != uri && String.IsNullOrEmpty(address.UserInfo) &&
                            (address.Scheme == "http" || address.Scheme == "https"))
                            options["all-proxy"] = address.AbsoluteUri;
                    }
                    catch (WebException) { /* aria2 can still use its environment proxy or a direct connection. */ }
                }
                string gid = (string)await session.CallAsync("aria2.addUri", token,
                    new object[] { uri.AbsoluteUri }, options).ConfigureAwait(false);
                string lastPath = null;
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    var state = (Dictionary<string, object>)await session.CallAsync("aria2.tellStatus", token,
                        gid, new[] { "status", "totalLength", "completedLength", "downloadSpeed", "connections", "files", "errorCode", "errorMessage" }).ConfigureAwait(false);
                    object[] files = ToArray(state["files"]);
                    if (files.Length != 0)
                    {
                        string candidate = Convert.ToString(((Dictionary<string, object>)files[0])["path"]);
                        if (!String.IsNullOrWhiteSpace(candidate)) lastPath = candidate;
                    }
                    string name = lastPath == null ? "正在识别文件名…" : SafeRemoteName(Path.GetFileName(lastPath));
                    if (String.IsNullOrEmpty(manualName) && String.IsNullOrEmpty(Path.GetExtension(name)))
                        name = QueryName(uri) ?? name;
                    if (progress != null) progress.Report(new DownloadProgress {
                        Name = name, Total = Number(state, "totalLength"), Completed = Number(state, "completedLength"),
                        Speed = Number(state, "downloadSpeed"), Connections = (int)Number(state, "connections")
                    });
                    string status = (string)state["status"];
                    if (status == "error")
                        throw new IOException(FriendlyError(Convert.ToString(state["errorCode"]), Convert.ToString(state["errorMessage"])));
                    if (status == "removed") throw new OperationCanceledException(token);
                    if (status == "complete")
                    {
                        token.ThrowIfCancellationRequested();
                        if (lastPath == null || !File.Exists(lastPath)) throw new IOException("下载结束，但未找到文件。请检查磁盘空间及安全软件提示。");
                        string fullSource = Path.GetFullPath(lastPath);
                        if (!fullSource.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                            throw new IOException("下载引擎返回了保存目录以外的路径，已停止保存。");
                        published = Publish(fullSource, folder, String.IsNullOrEmpty(manualName) ? name : manualName);
                        return new DownloadResult { Path = published, Bytes = new FileInfo(published).Length };
                    }
                    await Task.Delay(400, token).ConfigureAwait(false);
                }
            }
            finally
            {
                if (session != null) session.Dispose();
                try { Directory.Delete(staging, true); }
                catch (IOException) { /* Only this task's private temporary directory may remain. */ }
                catch (UnauthorizedAccessException) { }
            }
        }

        static object[] ToArray(object value)
        {
            var a = value as object[];
            if (a != null) return a;
            var list = value as ArrayList;
            return list == null ? new object[0] : list.ToArray();
        }
        static long Number(Dictionary<string, object> map, string key)
        {
            object value; long result;
            return map.TryGetValue(key, out value) && Int64.TryParse(Convert.ToString(value), out result) ? result : 0;
        }
        static string FriendlyError(string code, string detail)
        {
            switch (code)
            {
                case "3": return "找不到这个文件。请检查链接是否已过期，或重新复制发布页中的下载链接。";
                case "4": return "找不到这个文件。服务器多次返回文件不存在，请重新获取下载链接。";
                case "6": return "网络连接失败，请检查网络或系统代理后重试。";
                case "9": return "磁盘空间不足，请更换保存位置或清理空间。";
                case "22": return "服务器拒绝了请求。可能需要登录，或链接已过期；请重新获取公开下载直链。";
                case "24": return "链接需要账号密码，目前仅支持无需登录的 HTTP/HTTPS 文件直链。";
                default:
                    if (detail.IndexOf("certificate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        detail.IndexOf("SSL", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        detail.IndexOf("TLS", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "安全连接失败。请检查系统日期、证书和代理，程序不会跳过证书校验。";
                    return "下载未完成（引擎代码 " + code + "）。请检查网络、磁盘空间及链接有效期后重试。";
            }
        }
    }

    sealed class AriaSession : IDisposable
    {
        readonly string enginePath;
        readonly string secret = Guid.NewGuid().ToString("N");
        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        Process process;
        string endpoint;
        public AriaSession(string enginePath) { this.enginePath = enginePath; }

        public async Task StartAsync(CancellationToken token)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            endpoint = "http://127.0.0.1:" + port + "/jsonrpc";
            var info = new ProcessStartInfo(enginePath,
                "--no-conf=true --enable-rpc=true --rpc-listen-all=false --rpc-allow-origin-all=false" +
                " --rpc-listen-port=" + port + " --rpc-secret=" + secret +
                " --stop-with-process=" + Process.GetCurrentProcess().Id +
                " --disable-ipv6=true --enable-dht=false --enable-dht6=false --enable-peer-exchange=false" +
                " --follow-torrent=false --follow-metalink=false --check-certificate=true --auto-save-interval=1" +
                " --console-log-level=error --summary-interval=0 --download-result=hide --quiet=true") {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(enginePath),
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            process = Process.Start(info);
            process.OutputDataReceived += delegate { };
            process.ErrorDataReceived += delegate { };
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            for (int i = 0; i < 60; i++)
            {
                token.ThrowIfCancellationRequested();
                if (process.HasExited) throw new IOException("下载引擎未能启动，请检查 engine 文件夹及安全软件提示。");
                try { await CallAsync("aria2.getVersion", token).ConfigureAwait(false); return; }
                catch (WebException) { }
                await Task.Delay(100, token).ConfigureAwait(false);
            }
            throw new IOException("下载引擎启动超时，请关闭程序后重试。");
        }

        public async Task<object> CallAsync(string method, CancellationToken token, params object[] args)
        {
            var parameters = new List<object> { "token:" + secret };
            parameters.AddRange(args);
            byte[] bytes = Encoding.UTF8.GetBytes(json.Serialize(new { jsonrpc = "2.0", id = "1", method = method, @params = parameters }));
            var request = (HttpWebRequest)WebRequest.Create(endpoint);
            request.Proxy = null; request.Method = "POST"; request.ContentType = "application/json";
            request.ContentLength = bytes.Length; request.KeepAlive = false;
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(4000);
                using (deadline.Token.Register(request.Abort))
                {
                    try
                    {
                        using (Stream stream = await request.GetRequestStreamAsync().ConfigureAwait(false))
                            await stream.WriteAsync(bytes, 0, bytes.Length, deadline.Token).ConfigureAwait(false);
                        using (var response = await request.GetResponseAsync().ConfigureAwait(false))
                        using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                        {
                            var result = json.Deserialize<Dictionary<string, object>>(await reader.ReadToEndAsync().ConfigureAwait(false));
                            if (result.ContainsKey("error")) throw new IOException("下载引擎拒绝了操作，请重启程序后重试。");
                            return result["result"];
                        }
                    }
                    catch (WebException)
                    {
                        token.ThrowIfCancellationRequested();
                        throw;
                    }
                }
            }
        }

        public void Dispose()
        {
            if (process == null) return;
            try { if (!process.HasExited) { process.Kill(); process.WaitForExit(3000); } }
            catch (InvalidOperationException) { }
            finally { process.Dispose(); }
        }
    }
}
