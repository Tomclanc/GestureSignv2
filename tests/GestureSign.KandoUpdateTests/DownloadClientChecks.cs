using System.Net;
using GestureSign.WinUI;

internal static class DownloadClientChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        var file = Path.Combine(Path.GetTempPath(), "KandoDownloadTest-" + Guid.NewGuid() + ".zip");
        try
        {
            using var handler = new Handler();
            using var http = new HttpClient(handler);
            var url = new Uri("https://github.com/kando-menu/kando/releases/download/v3.0.0/test.zip");
            HttpResponseMessage Body(byte[] bytes, int length)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
                response.Content.Headers.ContentLength = length;
                return response;
            }
            handler.Replies.Enqueue(() => Body(new byte[] { 9 }, 10));
            handler.Replies.Enqueue(() => Body(new byte[] { 1, 2, 3 }, 3));
            await KandoDownloadClient.DownloadAsync(http, url, file, null, default);
            check(handler.Calls == 2, "Truncated download retries");
            check(File.ReadAllBytes(file).SequenceEqual(new byte[] { 1, 2, 3 }), "Retry truncates partial file rather than mixing archives");
            handler.Replies.Enqueue(() => new(HttpStatusCode.Forbidden));
            try { await KandoDownloadClient.DownloadAsync(http, url, file, null, default); throw new Exception("Expected forbidden"); }
            catch (HttpRequestException) { check(handler.Calls == 3, "Download authorization failure does not retry"); }
            for (var i = 0; i < 3; i++)
                handler.Replies.Enqueue(() => new(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) });
            try { await KandoDownloadClient.DownloadAsync(http, url, file, null, default, TimeSpan.FromMilliseconds(30)); throw new Exception("Expected idle timeout"); }
            catch (OperationCanceledException) { check(handler.Calls == 6, "Stalled body download times out with at most three attempts"); }
            using (File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                check(true, "Failed download releases file handles for cleanup");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try { await KandoDownloadClient.DownloadAsync(http, url, file, null, cancelled.Token); throw new Exception("Expected cancellation"); }
            catch (OperationCanceledException) { check(handler.Calls <= 7, "Caller cancellation never retries download"); }
        }
        finally { File.Delete(file); }
    }
    private sealed class Handler : HttpMessageHandler
    {
        internal int Calls;
        internal Queue<Func<HttpResponseMessage>> Replies = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(Replies.Dequeue()());
        }
    }
    private sealed class StalledStream : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }
}
