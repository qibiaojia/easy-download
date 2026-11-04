using System;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
using EasyDownload;

// Console-only harness exercising the same production core as the desktop application.
static class SmokeTests
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        var json = new JavaScriptSerializer();
        using (var cancel = new CancellationTokenSource())
        {
            int maxConnections = 0;
            try
            {
                if (args.Length < 4) throw new ArgumentException("engine url folder filename [cancelMs]");
                if (args.Length > 4 && Int32.Parse(args[4]) > 0) cancel.CancelAfter(Int32.Parse(args[4]));
                var progress = new SynchronousProgress(delegate(DownloadProgress p) { maxConnections = Math.Max(maxConnections, p.Connections); });
                var result = new DownloadCore(args[0]).DownloadAsync(args[1], args[2], args[3], progress, cancel.Token).GetAwaiter().GetResult();
                Console.WriteLine(json.Serialize(new { ok = true, path = result.Path, bytes = result.Bytes, maxConnections = maxConnections }));
                return 0;
            }
            catch (OperationCanceledException)
            { Console.WriteLine(json.Serialize(new { ok = false, canceled = true })); return 2; }
            catch (Exception ex)
            { Console.WriteLine(json.Serialize(new { ok = false, error = ex.Message, type = ex.GetType().Name })); return 1; }
        }
    }
    sealed class SynchronousProgress : IProgress<DownloadProgress>
    {
        readonly Action<DownloadProgress> callback;
        public SynchronousProgress(Action<DownloadProgress> callback) { this.callback = callback; }
        public void Report(DownloadProgress value) { callback(value); }
    }
}
