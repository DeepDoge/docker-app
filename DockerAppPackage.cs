using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;

class DockerAppPackage
{
    public class Manifest
    {
        public class BuildOptions
        {
            public string[] from { get; set; }
            public string[] run { get; set; }
            public Dictionary<string, string> environment { get; set; }
            public string[] addToPATH { get; set; }
        }

        public class BridgeOptions
        {
            public Dictionary<string, string> @in { get; set; }
            public Dictionary<string, string> @out { get; set; }
        }

        public class RunOptions
        {
            public string entryCommand { get; set; }
            public string[] persistentPaths { get; set; }
            public BridgeOptions bridge { get; set; }
        }

        public string name { get; set; }
        public string friendlyName { get; set; }
        public BuildOptions build { get; set; }
        public RunOptions run { get; set; }
    }

    public Manifest manifest { get; }
    public DockerApp dockerApp { get; }
    public string unpackedPath { get; }

    public static string Pack(string nonpackedAppDirPath, string packagePath)
    {
        packagePath = $"{packagePath}.dockerApp";
        if (File.Exists(packagePath)) File.Delete(packagePath);
        ZipFile.CreateFromDirectory(nonpackedAppDirPath, packagePath);
        return packagePath;
    }

    public DockerAppPackage(string dockerAppPackagePath) // normally this is getting package
    {
        if (!File.Exists(dockerAppPackagePath)) throw new Exception();
        unpackedPath = Path.Combine(Path.GetTempPath(), $"dockerapp-{Path.GetFileNameWithoutExtension(dockerAppPackagePath)}-{Path.GetRandomFileName()}");
        if (Directory.Exists(unpackedPath))
            Directory.Delete(unpackedPath, true);
        ZipFile.ExtractToDirectory(dockerAppPackagePath, unpackedPath);
        manifest = JsonConvert.DeserializeObject<Manifest>(File.ReadAllText($"{unpackedPath}/manifest.json"));
        dockerApp = new DockerApp(manifest.name);

        Program.CliExitEvent += () => Directory.Delete(unpackedPath, true); 
    }

    public DockerAppPackage(DockerApp dockerApp)
    {
        unpackedPath = Path.Combine(Path.GetTempPath(), $"dockerapp-{Path.GetFileNameWithoutExtension(dockerApp.appName)}-{Path.GetRandomFileName()}");
        CopyFilesRecursively(dockerApp.builderPath, unpackedPath);
        manifest = JsonConvert.DeserializeObject<Manifest>(File.ReadAllText($"{unpackedPath}/manifest.json"));
        this.dockerApp = dockerApp;

        Program.CliExitEvent += () => Directory.Delete(unpackedPath, true);
    }

    public string Install()
    {
        if (Directory.Exists(dockerApp.builderPath)) Directory.Delete(dockerApp.builderPath, true);
        
        Directory.CreateDirectory(dockerApp.builderPath);
        Debug.Print(unpackedPath);
        CopyFilesRecursively($"{unpackedPath}/temp", $"{dockerApp.builderPath}/temp");
        File.Copy($"{unpackedPath}/manifest.json", $"{dockerApp.builderPath}/manifest.json");

        File.WriteAllText(dockerApp.dockerFilePath, GenerateDockerFile());
        File.WriteAllText(dockerApp.bridgeFilePath, GenerateBridgeFile());
        MakeFileExecutable(dockerApp.bridgeFilePath);
        File.WriteAllText(dockerApp.entryFilePath, GenerateEntryFile());
        MakeFileExecutable(dockerApp.entryFilePath);

        Directory.CreateDirectory(DockerApp.BridgeOutPath);
        foreach (var bridgeOut in manifest.run.bridge.@out)
        {
            var outFilePath = $"{DockerApp.BridgeOutPath}/{bridgeOut.Value}";
            File.WriteAllText(outFilePath, GenerateBridgeOutFile(bridgeOut.Key));
        }

        if (Directory.Exists(dockerApp.appConfigPath)) Directory.Delete(dockerApp.appConfigPath, true);
        Directory.CreateDirectory(dockerApp.appConfigPath);

        Directory.CreateDirectory(dockerApp.bridgeInPath);        
        foreach (var bridgeIn in manifest.run.bridge.@in)
        {
            var bridgeInFilePath = $"{dockerApp.bridgeInPath}/{bridgeIn}";
            File.WriteAllText(bridgeInFilePath, $"{bridgeIn} $@");
            MakeFileExecutable(bridgeInFilePath);
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                UseShellExecute = true,
                FileName = "/bin/docker",
                Arguments = $"build -t dockerapp-{manifest.name} {dockerApp.builderPath}"
            },
        };
        process.Start();
        process.WaitForExit();

        return dockerApp.appPath;
    }

    private void CopyFilesRecursively(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(targetPath);
        //Now Create all of the directories
        foreach (string dirPath in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dirPath.Replace(sourcePath, targetPath));
        }

        //Copy all the files & Replaces any files with the same name
        foreach (string newPath in Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories))
        {
            File.Copy(newPath, newPath.Replace(sourcePath, targetPath), true);
        }
    }

    private void MakeFileExecutable(string filePath)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                UseShellExecute = true,
                FileName = "/bin/chmod",
                Arguments = $"+x \"{filePath}\""
            }
        };
        process.Start();
        process.WaitForExit();
    }

    private string GenerateBridgeOutFile(string @out)
    {
        var bridgeFile = new StringBuilder();

        bridgeFile.AppendLine($"{dockerApp.bridgeFilePath} {@out} $@");

        return bridgeFile.ToString();
    }

    private string GenerateDockerFile()
    {
        var dockerFile = new StringBuilder();

        foreach (var from in manifest.build.from)
            dockerFile.AppendLine($"FROM {from}");

        dockerFile.AppendLine("COPY ./temp /app/temp");
        dockerFile.AppendLine($"ENV PATH={dockerApp.bridgeInPath}:${{PATH}}");

        foreach (var run in manifest.build.run)
            dockerFile.AppendLine($"RUN {run}");

        dockerFile.AppendLine("RUN rm -r /app/temp");

        return dockerFile.ToString();
    }

    private string GenerateBridgeFile()
    {
        var bridgeFile = new StringBuilder();
    
        bridgeFile.AppendLine($"if ! [[ -n \"$( docker ps -q -f name=dockerapp-{dockerApp.appName} )\" ]]; then");
        bridgeFile.AppendLine("echo \"App container is not running, running it...\"");
        bridgeFile.AppendLine($"mkdir -p {dockerApp.appDataPath}");
        bridgeFile.AppendLine($"mkdir -p {dockerApp.appDataPath}/home");
        bridgeFile.AppendLine($"mkdir -p {dockerApp.appDataPath}/tmp");

        bridgeFile.AppendLine("xhost +local:root");
        bridgeFile.AppendLine("docker run -d\\");
        bridgeFile.AppendLine("\t--net host \\"); // use host network
        bridgeFile.AppendLine("\t--privileged \\"); // kernel access
        bridgeFile.AppendLine("\t-e DISPLAY=unix$DISPLAY \\"); // xserver display
        
        bridgeFile.AppendLine($"\t-e HOME={dockerApp.appDataPath}/home \\"); // set $HOME path
        bridgeFile.AppendLine("\t--volume=\"/$HOME:/$HOME\" \\"); // share host's home
        bridgeFile.AppendLine($"\t--volume=\"{dockerApp.appPath}:{dockerApp.appPath}:ro\" \\"); // persistent container home
        bridgeFile.AppendLine($"\t--volume=\"{dockerApp.appDataPath}/home:{dockerApp.appDataPath}/home\" \\"); // persistent container home
        bridgeFile.AppendLine($"\t--volume=\"{dockerApp.appDataPath}/tmp:/tmp\" \\"); // persistent /tmp
        
        bridgeFile.AppendLine("\t--volume=\"/mnt:/mnt\" \\"); // mounted
        bridgeFile.AppendLine("\t--volume=\"/media:/media\" \\"); // media
        bridgeFile.AppendLine("\t--volume=\"/cdrom:/cdrom\" \\"); // cdrom

        bridgeFile.AppendLine("\t--volume=\"/sys:/sys:ro\" \\"); // idk just added it here
        bridgeFile.AppendLine("\t--volume=\"/tmp/.X11-unix:/tmp/.X11-unix\" \\"); // its suppose to be x11 but not working
        bridgeFile.AppendLine("\t--volume=\"/dev:/dev\" \\"); // devices?
        bridgeFile.AppendLine("\t--device /dev/snd \\"); // suppose to fix sound but nope 

        bridgeFile.AppendLine("\t--volume=\"/usr/share/icons:/usr/share/icons:ro\" \\"); // icons
        bridgeFile.AppendLine("\t--volume=\"/usr/share/themes:/usr/share/themes:ro\" \\"); // themes

        bridgeFile.AppendLine("\t--workdir=\"$HOME\" \\"); // work dir on start, its host's home by default
        
        bridgeFile.AppendLine("\t--user $(id -u):$(id -g) \\"); // setting host's user as container's user
        bridgeFile.AppendLine("\t--volume=\"/etc/group:/etc/group:ro\" \\");
        bridgeFile.AppendLine("\t--volume=\"/etc/passwd:/etc/passwd:ro\" \\");
        bridgeFile.AppendLine("\t--volume=\"/etc/shadow:/etc/shadow:ro\" \\");
        
        bridgeFile.AppendLine($"\t--name dockerapp-{manifest.name} \\"); // container name (not image name)
        bridgeFile.AppendLine($"\t-ti dockerapp-{manifest.name} bash"); // image to run with the command
        bridgeFile.AppendLine("xhost -local:root"); // closing it like that might cause problems maybe idk
        bridgeFile.AppendLine("fi");


        bridgeFile.AppendLine($"if [[ -n \"$@\" ]]; then");
        bridgeFile.AppendLine($"docker exec -ti dockerapp-{manifest.name} $@");
        bridgeFile.AppendLine("else");
        bridgeFile.AppendLine($"docker exec -ti dockerapp-{manifest.name} bash");
        bridgeFile.AppendLine("fi");

        bridgeFile.AppendLine($"APP_PROCESSES=\"$( docker container top dockerapp-{dockerApp.appName} )\"");
        bridgeFile.AppendLine("arr=()");
        bridgeFile.AppendLine("while read -r line; do");
        bridgeFile.AppendLine("arr+=(\"$line\")");
        bridgeFile.AppendLine("done <<< \"$APP_PROCESSES\"");

        bridgeFile.AppendLine($"if [[ ( -n \"${{arr[2]}}\" ) && (\"$APP_PROCESSES\" = UID*) ]]; then");
        bridgeFile.AppendLine("echo \"App container is still running (used by another process).\"");
        bridgeFile.AppendLine("else");
        bridgeFile.AppendLine("echo \"App container is free, destroying container...\"");
        bridgeFile.AppendLine($"docker container stop dockerapp-{manifest.name}");
        bridgeFile.AppendLine($"docker container rm dockerapp-{manifest.name}"); // destroy container at the end
        bridgeFile.AppendLine("fi");

        return bridgeFile.ToString();
    }

    private string GenerateEntryFile()
    {
        var entryFile = new StringBuilder();

        entryFile.AppendLine($"{dockerApp.bridgeFilePath} {manifest.run.entryCommand} $@");

        return entryFile.ToString();
    }

}