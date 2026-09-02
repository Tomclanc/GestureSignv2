using System;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace GestureSign.WinUI.Services;

/// <summary>Small, UI-free bridge for the daemon named-pipe protocol.</summary>
internal sealed class DaemonBridgeService
{
    private readonly TimeSpan _connectTimeout;

    public DaemonBridgeService(TimeSpan? connectTimeout = null)
        => _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(1);

    public async Task<bool> SendAsync(byte command)
    {
        try
        {
            var user = WindowsIdentity.GetCurrent().User?.ToString();
            if (string.IsNullOrWhiteSpace(user))
                return false;

            await using var pipe = new NamedPipeClientStream(
                ".", $"GestureSignDaemon-{user}", PipeDirection.Out,
                PipeOptions.Asynchronous, TokenImpersonationLevel.None);
            using var cancellation = new CancellationTokenSource(_connectTimeout);
            await pipe.ConnectAsync(cancellation.Token);
            pipe.WriteByte(command);
            await pipe.FlushAsync(cancellation.Token);
            return true;
        }
        catch
        {
            // The daemon may be stopped while settings are open.
            return false;
        }
    }
}
