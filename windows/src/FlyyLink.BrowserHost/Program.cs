using FlyyLink.Core;

var endpointFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlyyLink", "Browser", "endpoint.json");

BrowserEndpoint? Endpoint()
{
    try
    {
        return File.Exists(endpointFile) ? BrowserEndpoint.Parse(File.ReadAllText(endpointFile)) : null;
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
    {
        return null;
    }
}

void Launch()
{
    try
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "FlyyLink.exe")) { UseShellExecute = true });
    }
    catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
    {
    }
}

await new BrowserHostRelay(Console.OpenStandardInput(), Console.OpenStandardOutput(), Endpoint) { Launch = Launch }.RunAsync();
