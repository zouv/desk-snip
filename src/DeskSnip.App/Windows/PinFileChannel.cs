using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DeskSnip.App.Windows;

internal sealed class PinFileChannel : IDisposable
{
    private readonly string name;
    private readonly Action<string> receive;
    private readonly object gate = new object();
    private readonly PipeSecurity security;
    private readonly Task listening;
    private NamedPipeServerStream? active;
    private bool stopped;
    internal PinFileChannel(string identity, Action<string> receive)
    {
        name = "DeskSnip.PinFile." + identity; this.receive = receive;
        security = new PipeSecurity(); security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        active = CreatePipe(); listening = Task.Run(ListenAsync);
    }
    internal static void Send(string identity, string path)
    {
        var bytes = Encoding.UTF8.GetBytes(path); if (bytes.Length == 0 || bytes.Length > 32768) throw new ArgumentException(L.Get("图像路径需要 1–32768 个 UTF-8 字节。"));
        using (var pipe = new NamedPipeClientStream(".", "DeskSnip.PinFile." + identity, PipeDirection.Out))
        { pipe.Connect(3000); using (var writer = new BinaryWriter(pipe, Encoding.UTF8, true)) { writer.Write(bytes.Length); writer.Write(bytes); writer.Flush(); } }
    }
    private NamedPipeServerStream CreatePipe() => new NamedPipeServerStream(name, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
    private static async Task ReadExactly(Stream pipe, byte[] bytes, CancellationToken cancellation)
    {
        var offset = 0;
        while (offset < bytes.Length)
        {
            var count = await pipe.ReadAsync(bytes, offset, bytes.Length - offset, cancellation).ConfigureAwait(false);
            if (count == 0) throw new EndOfStreamException(); offset += count;
        }
    }
    private async Task ListenAsync()
    {
        while (true)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                lock (gate) { if (stopped) return; pipe = active ?? (active = CreatePipe()); }
                await pipe.WaitForConnectionAsync().ConfigureAwait(false);
                using (var timeout = new CancellationTokenSource(3000))
                using (timeout.Token.Register(pipe.Dispose))
                {
                    var header = new byte[4]; await ReadExactly(pipe, header, timeout.Token).ConfigureAwait(false);
                    var length = BitConverter.ToInt32(header, 0); if (length <= 0 || length > 32768) continue;
                    var bytes = new byte[length]; await ReadExactly(pipe, bytes, timeout.Token).ConfigureAwait(false);
                    timeout.Token.ThrowIfCancellationRequested(); receive(new UTF8Encoding(false, true).GetString(bytes));
                }
            }
            catch (Exception error) when (error is IOException || error is ObjectDisposedException || error is OperationCanceledException || error is DecoderFallbackException) { }
            finally { lock (gate) { pipe?.Dispose(); if (active == pipe) active = null; } }
            if (pipe == null) await Task.Delay(100).ConfigureAwait(false);
        }
    }
    public void Dispose()
    {
        lock (gate) { stopped = true; active?.Dispose(); active = null; }
        listening.GetAwaiter().GetResult();
    }
}
