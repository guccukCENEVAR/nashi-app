using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text.Json;
namespace Nashi.Linux;

/// One Nashi per user session. Later launches forward their files over a Unix
/// socket in the user's private runtime directory, then exit.
internal sealed class SingleInstance:IDisposable
{
    private readonly FileStream? lockFile;private readonly string socketPath;private readonly CancellationTokenSource lifetime=new();private Socket? listener;
    internal bool Primary{get;}
    internal SingleInstance(string? testDirectory=null)
    {
        var directory=testDirectory??RuntimeDirectory();Directory.CreateDirectory(directory);
        socketPath=Path.Combine(directory,"nashi.sock");
        try{lockFile=new FileStream(Path.Combine(directory,"nashi.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);Primary=true;}
        catch(IOException){Primary=false;}
    }
    private static string RuntimeDirectory()
    {
        // XDG_RUNTIME_DIR is private (0700) to the user; fall back to a private folder in the user's cache.
        var runtime=Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if(!string.IsNullOrEmpty(runtime)&&Path.IsPathFullyQualified(runtime)&&Directory.Exists(runtime))return Path.Combine(runtime,"nashi");
        var fallback=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".cache","nashi");
        Directory.CreateDirectory(fallback);File.SetUnixFileMode(fallback,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);return fallback;
    }
    // Unix socket paths are limited to 108 bytes including the terminator.
    private bool SocketUsable=>System.Text.Encoding.UTF8.GetByteCount(socketPath)<104;
    /// Starts listening for later launches. Failure only disables forwarding; Nashi keeps running.
    internal void Start(Action<string[]> receive)
    {
        if(!Primary)throw new InvalidOperationException("Only the primary instance can listen.");
        if(!SocketUsable){Console.Error.WriteLine("Nashi: the runtime directory path is too long for a launch socket; files opened later start a new window.");return;}
        try
        {
            if(File.Exists(socketPath))File.Delete(socketPath);
            listener=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);listener.Bind(new UnixDomainSocketEndPoint(socketPath));
            File.SetUnixFileMode(socketPath,UnixFileMode.UserRead|UnixFileMode.UserWrite);listener.Listen(4);
        }
        catch(Exception error)when(error is SocketException or IOException or UnauthorizedAccessException){listener?.Dispose();listener=null;Console.Error.WriteLine("Nashi: launch forwarding is unavailable: "+error.Message);return;}
        var server=listener;
        _ = Task.Run(async()=>
        {
            while(!lifetime.IsCancellationRequested)
            {
                try
                {
                    using var connection=await server.AcceptAsync(lifetime.Token);await using var stream=new NetworkStream(connection,true);
                    using var timeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);timeout.CancelAfter(TimeSpan.FromSeconds(4));
                    var prefix=new byte[4];await stream.ReadExactlyAsync(prefix,timeout.Token);var length=BinaryPrimitives.ReadInt32LittleEndian(prefix);if(length is <2 or >131072)continue;
                    var data=new byte[length];await stream.ReadExactlyAsync(data,timeout.Token);var paths=JsonSerializer.Deserialize<string[]>(data);
                    if(paths is null||paths.Length>256||paths.Any(p=>p is null||p.Length>16384))continue;
                    receive(paths);await stream.WriteAsync(new byte[]{1},timeout.Token);
                }
                catch(OperationCanceledException)when(lifetime.IsCancellationRequested){break;}
                catch(Exception error)when(error is IOException or SocketException or JsonException or OperationCanceledException or ObjectDisposedException){if(!lifetime.IsCancellationRequested)await Task.Delay(100);}
            }
        });
    }
    internal async Task Notify(string[] paths)
    {
        if(!SocketUsable)throw new IOException("The launch socket path is too long.");
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(4));
        using var client=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);await client.ConnectAsync(new UnixDomainSocketEndPoint(socketPath),timeout.Token);
        await using var stream=new NetworkStream(client,false);var data=JsonSerializer.SerializeToUtf8Bytes(paths.Take(256));if(data.Length>131072)throw new IOException("Choose fewer files to open.");
        var prefix=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(prefix,data.Length);await stream.WriteAsync(prefix,timeout.Token);await stream.WriteAsync(data,timeout.Token);
        var ack=new byte[1];await stream.ReadExactlyAsync(ack,timeout.Token);
    }
    public void Dispose()
    {
        lifetime.Cancel();listener?.Dispose();
        if(Primary){try{if(File.Exists(socketPath))File.Delete(socketPath);}catch(IOException){}}
        lockFile?.Dispose();
    }
}
