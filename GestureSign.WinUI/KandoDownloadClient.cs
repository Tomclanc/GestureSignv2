using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GestureSign.WinUI;

internal static class KandoDownloadClient
{
    internal static async Task DownloadAsync(HttpClient client, Uri url, string archivePath,
        IProgress<double>? progress, CancellationToken token, TimeSpan? idleTimeout = null)
    {
        var idle = idleTimeout ?? TimeSpan.FromSeconds(30);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                progress?.Report(0);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(idle);
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                response.EnsureSuccessStatusCode();
                var totalLength = response.Content.Headers.ContentLength;
                await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
                await using var output = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, true);
                var buffer = new byte[128 * 1024];
                long received = 0;
                while (true)
                {
                    timeout.CancelAfter(idle);
                    var count = await input.ReadAsync(buffer, timeout.Token);
                    if (count == 0) break;
                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                    received += count;
                    if (totalLength is > 0) progress?.Report(received * 92d / totalLength.Value);
                }
                if (totalLength is > 0 && received != totalLength.Value)
                    throw new EndOfStreamException($"Kando download ended early ({received}/{totalLength.Value} bytes).");
                return;
            }
            catch (Exception error) when (attempt < 3 && !token.IsCancellationRequested &&
                (error is IOException or OperationCanceledException || error is HttpRequestException request &&
                 (request.StatusCode is null || (int)request.StatusCode >= 500)))
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt), token);
            }
        }
    }
}
