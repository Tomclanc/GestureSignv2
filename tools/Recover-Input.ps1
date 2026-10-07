[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
# This requests cancellation and recovery, never executes an unfinished gesture.
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$pipe = [IO.Pipes.NamedPipeClientStream]::new('.', "GestureSignDaemon-$sid", [IO.Pipes.PipeDirection]::Out)
try {
    $pipe.Connect(1500)
    $pipe.WriteByte(14) # IpcCommands.RecoverInput
    $pipe.Flush()
} finally { $pipe.Dispose() }
Write-Output 'Input recovery requested. Check GestureSign.log for Input recovery completed.'