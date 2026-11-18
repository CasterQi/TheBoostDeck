using System.IO.Pipes;

namespace TheBoostDeck.App;

/// <summary>
/// Lets a second TheBoostDeck process (for example one started by double-clicking a
/// .boostskin file) hand the package path to the instance that is already running.
/// </summary>
internal static class SkinImportChannel
{
    private const string PipeName = "TheBoostDeck.SkinImport";
    private const int ConnectTimeoutMs = 2500;

    public static bool TrySend(string path)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            client.Connect(ConnectTimeoutMs);
            using var writer = new StreamWriter(client);
            writer.WriteLine(path);
            writer.Flush();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void Listen(Action<string> onPath, CancellationToken token)
    {
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                    using var reader = new StreamReader(server);
                    var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(line))
                        onPath(line.Trim());
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception)
                {
                    try
                    {
                        await Task.Delay(500, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }, token);
    }
}
