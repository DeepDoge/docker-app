using System;
using System.Reflection;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

class Commands
{
    private static BindingFlags _BindingFlags { get; } = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
    private static Type _Type { get; } = typeof(Commands);
    private static Dictionary<string, MethodInfo> _Methods { get; } = (new Func<Dictionary<string, MethodInfo>>(() =>
    {
        var dic = new Dictionary<string, MethodInfo>();
        var methods = _Type.GetMethods(_BindingFlags);
        foreach (var method in methods)
            if (char.IsLower(method.Name[0]))
                dic[method.Name] = method;
        return dic;
    }))();

    public object Run(string command, string[] parameters) => Run<object>(command, parameters);
    public T Run<T>(string command, string[] parameters)
    {
        var method = _Methods[command];
        if (typeof(T) != typeof(Object) && typeof(T) != method.ReturnType)
            throw new Exception($"Method return type is \"{method.ReturnType.FullName}\", expected {typeof(T).FullName}.");

        var parameterInfos = method.GetParameters();
        var parametersObject = new object[parameterInfos.Length];
        for (var i = 0; i < parametersObject.Length; i++)
        {
            if (i >= parameters.Length) parametersObject[i] = parameterInfos[i].DefaultValue;
            else switch (parameterInfos[i].ParameterType.FullName)
                {
                    case "System.String":
                        parametersObject[i] = parameters[i];
                        break;
                    case "System.Int32":
                        parametersObject[i] = Int32.Parse(parameters[i]);
                        break;
                    case "System.Int64":
                        parametersObject[i] = Int64.Parse(parameters[i]);
                        break;
                    case "System.Single":
                        parametersObject[i] = Single.Parse(parameters[i]);
                        break;
                    case "System.Double":
                        parametersObject[i] = Double.Parse(parameters[i]);
                        break;
                    case "System.Boolean":
                        parametersObject[i] = Boolean.Parse(parameters[i]);
                        break;
                    default:
                        throw new Exception($"Unknown parameter type \"{parameterInfos[i].ParameterType.FullName}\"");
                }
        }
        return (T)method.Invoke(this, parametersObject);
    }

    public string help()
    {
        var text = new StringBuilder();
        foreach (var method in _Methods.Values)
        {
            var parameters = (from param in method.GetParameters() select $"{param.Name}{(param.HasDefaultValue ? "?" : "")}:{param.ParameterType.Name}");
            text.AppendLine($"{method.Name} ({string.Join(", ", parameters)}) => {method.ReturnType.Name}");
        }
        return text.ToString();
    }

    public string install(string dockerAppPackagePath) => new DockerAppPackage(dockerAppPackagePath).Install();
    public string pack(string nonpackedAppDirPath, string packagePath = null) => DockerAppPackage.Pack(nonpackedAppDirPath, packagePath ?? nonpackedAppDirPath.TrimEnd('/'));
    public void bridge(string appName, string command = "") => new DockerApp(appName).Bridge(string.Join(" ", Program.Args.Skip(2)));
    public void run(string appName, string args = null) => new DockerApp(appName).Run(string.Join(" ", Program.Args.Skip(2)));
    public void cleardata(string appName) => new DockerApp(appName).ClearData();
    public void clearcache(string appName) => new DockerApp(appName).ClearCache();
    public void rebuild(string appName) => new DockerApp(appName).Rebuild();
    
}