using System.Net;

var root = new DirectoryInfo(AppContext.BaseDirectory);
while (root != null && !File.Exists(Path.Combine(root.FullName, "RaceImperialis.slnx"))) root = root.Parent;
if (root == null) throw new DirectoryNotFoundException("Could not find the Race Imperialis repository root.");
var checks = await RacingProjectChecks.RunAsync(root.FullName);
checks += await GraphicsOptionsChecks.RunAsync();
Console.WriteLine($"PASS: {checks} Race Imperialis assertions (assets, countdown, difficulties, driving, pause, restart, finishing, and graphics preferences).");

sealed class FileHandler(string root) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var path = request.RequestUri!.AbsolutePath.TrimStart('/');
        var file = Path.Combine(root, path);
        return Task.FromResult(File.Exists(file)
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(File.ReadAllText(file), System.Text.Encoding.UTF8, "application/json")
            }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
