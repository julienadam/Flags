using System.Diagnostics;
using System.Text.Json;

class Program
{
    public static void Main()
    {
        var source = new CancellationTokenSource();
        var work = Work(source.Token);
        var waitForCancel = WaitForUserCancelTask(source);

        Console.WriteLine("Process started, press enter to cancel");
        Task.WaitAny(work, waitForCancel);

        Console.WriteLine("Starting cleanup phase");
        source.Cancel();
        Task.WaitAll(work, waitForCancel);
    }

    private static async Task WaitForUserCancelTask(CancellationTokenSource source)
    {
        await Console.In.ReadLineAsync(source.Token);
        Console.WriteLine("Cancelling");
        source.Cancel();
    }

    private static void WaitForUserCancelPolling(CancellationTokenSource source)
    {
        // polling every 100 ms 
        while(!source.IsCancellationRequested)
        {
            if (Console.KeyAvailable && Console.ReadKey().Key == ConsoleKey.Enter)
            {
                Console.WriteLine("Enter pressed, cancelling");
                source.Cancel();
                return;
            }
            Thread.Sleep(TimeSpan.FromMilliseconds(100));
        }
    }

    private static async Task Work(CancellationToken token)
    {
        var rootDir = @"c:\temp\countries";
        if (!Directory.Exists(rootDir))
        {
            Directory.CreateDirectory(rootDir);
        }

        PrintWithThreadId($"Starting");
        var url = "https://formationdataaccount.blob.core.windows.net/formationdata/eu.json";
        var client = new HttpClient();

        PrintWithThreadId($"Getting stream from {url}");
        using var stream = await client.GetStreamAsync(url, token);
        if (token.IsCancellationRequested) return;
        PrintWithThreadId($"Stream retrieved from {url}, deserializing");

        var countries = await JsonSerializer.DeserializeAsync<IEnumerable<Country>>(stream, cancellationToken: token);
        if (token.IsCancellationRequested) return;
        PrintWithThreadId($"countries deserialized, starting flag download");

        Task.WaitAll(
            countries!
                .Select(country =>
                    Task.Run(async () =>
                    {
                        if (token.IsCancellationRequested) return;

                        var flagUrl = country!.flag;
                        PrintWithThreadId($"Starting flag download for {country.name} at {country.flag}");
                        using var flagStream = await client.GetStreamAsync(flagUrl, token);
                        if (token.IsCancellationRequested) return;

                        var localFlagFileName = Path.Combine(rootDir, country.name + ".svg");
                        PrintWithThreadId($"Retrieved flag from {country.flag}, writing to {localFlagFileName}");
                        using (var localFlag = File.Create(localFlagFileName))
                        {
                            await flagStream.CopyToAsync(localFlag, token);
                            if (token.IsCancellationRequested) return;
                        }
                        PrintWithThreadId($"Flag written to {localFlagFileName}, opening in browser");

                        var psi = new ProcessStartInfo(localFlagFileName) { UseShellExecute = true };
                        Process.Start(psi);
                        PrintWithThreadId($"Browser opened on {localFlagFileName}");
                    }))
                .ToArray()
        , token);
        PrintWithThreadId("Finished");
    }

    public static void PrintWithThreadId(string info)
    {
        Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId} : {info}");
    }
}

public record Country
{
    public required string name { get; set; }
    public required string capital { get; set; }
    public required string flag { get; set; }
    public int population { get; set; }
    public double area { get; set; }
}