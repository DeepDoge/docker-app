using System;
using System.Diagnostics;
using System.IO;
using System.Text;

class DockerApp
{
    public static string HomePath { get; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string DockerAppPath { get; } = $"{HomePath}/.dockerapp";
    public static string BridgeOutPath { get; } = $"{DockerAppPath}/bridge-out-bin";

    public string appName { get; }
    public string appPath => $"{DockerAppPath}/apps/{appName}";
    public string appDataPath => $"{appPath}/current";
    public string appConfigPath => $"{appPath}/config";
    public string bridgeInPath => $"{appConfigPath}/bridge-in-bin";
    public string builderPath => $"{appPath}/builder";
    public string dockerFilePath => $"{builderPath}/Dockerfile";
    public string bridgeFilePath => $"{appPath}/bridge";
    public string entryFilePath => $"{appPath}/entry";

    public DockerApp(string appName)
    {
        this.appName = appName;
    }

    public void ClearData() => Directory.Delete(appDataPath, true);
    public void ClearCache() => Directory.Delete($"{appDataPath}/tmp", true);

    public void Rebuild() 
    {
        new DockerAppPackage(this).Install();
    }

    public void Run(string args) => ConnectContainer($"{entryFilePath} {args}");

    public void Bridge(string command) => ConnectContainer($"{bridgeFilePath} {command}");

    private void ConnectContainer(string args)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                UseShellExecute = true,
                FileName = "/bin/bash",
                Arguments = args,

            },
        };
        process.Start();
        process.WaitForExit();
    }
}