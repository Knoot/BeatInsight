using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace BeatInsight.Tooling;

// Linked into test/CLI projects only. No machine configuration enters the mod.
internal static class LocalArtifacts
{
    internal static string Root
    {
        get
        {
            var project=FindProjectRoot();
            var configured=Environment.GetEnvironmentVariable("BEATINSIGHT_LOCAL_ROOT");
            if(string.IsNullOrWhiteSpace(configured))
            {
                var settings=System.IO.Path.Combine(project,"paths.props");
                if(File.Exists(settings))configured=XDocument.Load(settings).Root?.Elements("PropertyGroup").SelectMany(g=>g.Elements("LocalArtifactsRoot")).FirstOrDefault()?.Value;
            }
            return System.IO.Path.GetFullPath(string.IsNullOrWhiteSpace(configured)?".local":configured!,project);
        }
    }
    internal static string PathFor(params string[] parts)=>System.IO.Path.Combine(new[]{Root}.Concat(parts).ToArray());
    static string FindProjectRoot()
    {
        foreach(var start in new[]{Directory.GetCurrentDirectory(),AppContext.BaseDirectory})
            for(var dir=new DirectoryInfo(start);dir!=null;dir=dir.Parent)
                if(File.Exists(System.IO.Path.Combine(dir.FullName,"Directory.Build.props"))&&File.Exists(System.IO.Path.Combine(dir.FullName,"tools","LocalSettings.ps1")))return dir.FullName;
        // Explicit root still supports running a built CLI away from its sources.
        if(!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BEATINSIGHT_LOCAL_ROOT")))return Directory.GetCurrentDirectory();
        throw new InvalidOperationException("Run from the repository or set BEATINSIGHT_LOCAL_ROOT.");
    }
}
