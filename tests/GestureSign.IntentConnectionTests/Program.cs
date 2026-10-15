using GestureSign.WinUI.Services;
using System.IO.Pipes;

int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine($"PASS {++checks}: {message}"); }
async Task DelayedPipe(bool alreadyRunning)
{
    string name = "GestureSign-readiness-test-" + Guid.NewGuid().ToString("N");
    int starts = 0, attempts = 0;
    var server = Task.Run(async () =>
    {
        await Task.Delay(500);
        using var pipe = new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await pipe.WaitForConnectionAsync();
        using var writer = new StreamWriter(pipe) { AutoFlush = true };
        await writer.WriteLineAsync("ready");
    });
    var result = await IntentHostReadiness.WaitAsync(async () =>
    {
        attempts++;
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.In, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(80);
        try { await pipe.ConnectAsync(timeout.Token); }
        catch (OperationCanceledException ex) { throw new NotReady(ex); }
        using var reader = new StreamReader(pipe);
        return await reader.ReadLineAsync();
    }, ex => ex is NotReady, () => alreadyRunning, () => starts++, TimeSpan.FromSeconds(3));
    await server;
    Check(result == "ready" && attempts > 1, "Delayed named pipe connects successfully");
    Check(starts == (alreadyRunning ? 0 : 1), "Existing engine is reused; missing engine starts only once");
}
await DelayedPipe(true);
await DelayedPipe(false);
int calls = 0;
try
{
    await IntentHostReadiness.WaitAsync<int>(() => { calls++; throw new IOException("Response lost after command dispatch"); },
        ex => ex is NotReady, () => true, () => throw new Exception("Unexpected start"), TimeSpan.FromSeconds(1));
    throw new Exception("Lost response was swallowed");
}
catch (IOException) { Check(calls == 1, "A dispatched command is never replayed after response failure"); }
var watch = System.Diagnostics.Stopwatch.StartNew();
try
{
    await IntentHostReadiness.WaitAsync<int>(() => throw new NotReady(new IOException()), ex => ex is NotReady,
        () => true, () => {}, TimeSpan.FromMilliseconds(200));
    throw new Exception("Unavailable engine was accepted");
}
catch (NotReady) { Check(watch.Elapsed < TimeSpan.FromSeconds(2), "Readiness wait has a bounded timeout"); }
sealed class NotReady(Exception inner) : IOException("Not ready", inner);
