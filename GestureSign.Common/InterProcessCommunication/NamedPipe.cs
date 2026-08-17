using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using System.IO.Pipes;
using System.IO;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Security.Principal;
using GestureSign.Common.Log;

namespace GestureSign.Common.InterProcessCommunication
{
    public class NamedPipe : IDisposable
    {
        private const int WireMagic = 0x31505347; // "GSP1"
        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool WaitNamedPipe(string name, int timeout);

        private static CustomNamedPipeServer _pipeServer;
        private static readonly NamedPipe instance = new NamedPipe();

        private bool disposed = false; // To detect redundant calls

        public static NamedPipe Instance
        {
            get
            {
                return instance;
            }
        }

        public static object ReadMessages(PipeStream pipe, out IpcCommands command)
        {
            using (MemoryStream memoryStream = new MemoryStream())
            {
                pipe.CopyTo(memoryStream);
                memoryStream.Seek(0, SeekOrigin.Begin);
                command = (IpcCommands)memoryStream.ReadByte();
                if (memoryStream.Length == memoryStream.Position)
                    return null;

                using (var reader = new BinaryReader(memoryStream, Encoding.UTF8, true))
                {
                    if (reader.ReadInt32() != WireMagic)
                        throw new InvalidDataException("Unsupported GestureSign IPC payload format.");

                    switch (command)
                    {
                        case IpcCommands.GotGesture:
                            return ReadPointPatterns(reader);
                        case IpcCommands.SynDeviceState:
                            return (Common.Input.Devices)reader.ReadInt32();
                        case IpcCommands.SynRecognitionState:
                            return reader.ReadBoolean();
                        default:
                            return null;
                    }
                }
            }
        }

        internal static void WriteMessage(Stream stream, IpcCommands command, object message)
        {
            stream.WriteByte((byte)command);
            if (message == null)
                return;

            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(WireMagic);
                switch (command)
                {
                    case IpcCommands.GotGesture:
                        WritePointPatterns(writer, (Point[][][])message);
                        break;
                    case IpcCommands.SynDeviceState:
                        writer.Write((int)(Common.Input.Devices)message);
                        break;
                    case IpcCommands.SynRecognitionState:
                        writer.Write((bool)message);
                        break;
                    default:
                        throw new InvalidDataException("IPC command does not define a payload format: " + command);
                }
            }
        }

        private static void WritePointPatterns(BinaryWriter writer, Point[][][] patterns)
        {
            writer.Write(patterns.Length);
            foreach (var pattern in patterns)
            {
                writer.Write(pattern.Length);
                foreach (var stroke in pattern)
                {
                    writer.Write(stroke.Length);
                    foreach (var point in stroke)
                    {
                        writer.Write(point.X);
                        writer.Write(point.Y);
                    }
                }
            }
        }

        private static Point[][][] ReadPointPatterns(BinaryReader reader)
        {
            var patterns = new Point[ReadLength(reader)][][];
            for (var patternIndex = 0; patternIndex < patterns.Length; patternIndex++)
            {
                var strokes = new Point[ReadLength(reader)][];
                patterns[patternIndex] = strokes;
                for (var strokeIndex = 0; strokeIndex < strokes.Length; strokeIndex++)
                {
                    var points = new Point[ReadLength(reader)];
                    strokes[strokeIndex] = points;
                    for (var pointIndex = 0; pointIndex < points.Length; pointIndex++)
                        points[pointIndex] = new Point(reader.ReadInt32(), reader.ReadInt32());
                }
            }
            return patterns;
        }

        private static int ReadLength(BinaryReader reader)
        {
            var length = reader.ReadInt32();
            if (length < 0 || length > 100000)
                throw new InvalidDataException("Invalid GestureSign IPC collection length.");
            return length;
        }

        private static bool WaitForNamedPipeConnection(string pipeName, int interval = 1000)
        {
            const int unit = 50;
            for (int i = 0; i < interval / unit; i++)
            {
                if (!NamedPipeDoesNotExist(pipeName))
                    return true;
                Thread.Sleep(unit);
            }
            return false;
        }

        public void RunNamedPipeServer(string pipeName, IMessageProcessor messageProcessor)
        {
            _pipeServer = new CustomNamedPipeServer(pipeName, messageProcessor);
        }

        public static Task<bool> SendMessageAsync(IpcCommands command, string pipeName, object message = null, bool wait = true)
        {
            string userPipeName = GetUserPipeName(pipeName);
            return Task.Run<bool>(new Func<bool>(() =>
               {
                   try
                   {
                       using (NamedPipeClientStream pipeClient = new NamedPipeClientStream(".", userPipeName, PipeDirection.Out, PipeOptions.None, TokenImpersonationLevel.None))
                       {
                           using (MemoryStream ms = new MemoryStream())
                           {
                               if (wait)
                               {
                                   if (!WaitForNamedPipeConnection(userPipeName))
                                       return false;
                               }
                               else if (NamedPipeDoesNotExist(userPipeName))
                               {
                                   return false;
                               }

                               pipeClient.Connect(10);

                               WriteMessage(ms, command, message);
                               ms.Seek(0, SeekOrigin.Begin);

                               ms.CopyTo(pipeClient);
                               pipeClient.Flush();
                               pipeClient.WaitForPipeDrain();
                           }
                       }
                       return true;
                   }
                   catch (IOException)
                   {
                       return false;
                   }
                   catch (TimeoutException)
                   {
                       return false;
                   }
                   catch (Exception e)
                   {
                       Logging.LogException(e);
                       return false;
                   }
               }));
        }

        public static Task<object> GetMessageAsync(string pipeName, int wait = 1000)
        {
            string userPipeName = GetUserPipeName(pipeName);
            return Task.Run(new Func<object>(() =>
            {
                try
                {
                    using (NamedPipeClientStream pipeClient = new NamedPipeClientStream(".", userPipeName, PipeDirection.In, PipeOptions.None, TokenImpersonationLevel.None))
                    {
                        using (MemoryStream ms = new MemoryStream())
                        {
                            if (wait > 0)
                            {
                                if (!WaitForNamedPipeConnection(userPipeName, wait))
                                    return null;
                            }
                            else if (NamedPipeDoesNotExist(userPipeName))
                            {
                                return null;
                            }

                            pipeClient.Connect(10);

                            object data = ReadMessages(pipeClient, out IpcCommands command);
                            return data;
                        }
                    }
                }
                catch (IOException)
                {
                    return null;
                }
                catch (TimeoutException)
                {
                    return null;
                }
                catch (Exception e)
                {
                    Logging.LogException(e);
                    return null;
                }
            }));
        }

        public static bool NamedPipeDoesNotExist(string pipeName)
        {
            try
            {
                const int timeout = 0;
                string normalizedPath = Path.GetFullPath(string.Format(@"\\.\pipe\{0}", pipeName));
                bool exists = WaitNamedPipe(normalizedPath, timeout);
                if (!exists)
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == 0) // pipe does not exist
                        return true;
                    else if (error == 2) // win32 error code for file not found
                        return true;
                    // all other errors indicate other issues
                }
                return false;
            }
            catch (Exception ex)
            {
                throw new Exception("Failure in WaitNamedPipe()", ex);
                //return true; // assume it exists
            }
        }

        public static string GetUserPipeName(string pipeName)
        {
            var currentUser = WindowsIdentity.GetCurrent();
            return pipeName + "-" + currentUser.User.ToString();
        }

        #region IDisposable Support

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    _pipeServer?.Dispose();
                }

                disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        #endregion

    }
}
