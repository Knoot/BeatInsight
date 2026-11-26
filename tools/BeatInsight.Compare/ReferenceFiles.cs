using System.Xml.Linq;

namespace BeatInsight.Compare;

static class ReferenceFiles
{
    public static string Get(string role,string gameRoot)
    {
        string setting="Reference"+role+"Assembly";
        string? value=Environment.GetEnvironmentVariable("BEATINSIGHT_REFERENCE_"+role.ToUpperInvariant()+"_ASSEMBLY");
        foreach(var start in new[]{Directory.GetCurrentDirectory(),AppContext.BaseDirectory})
        for(var folder=new DirectoryInfo(start);string.IsNullOrWhiteSpace(value)&&folder!=null;folder=folder.Parent)
        {
            string file=Path.Combine(folder.FullName,"paths.props");
            if(!File.Exists(file))continue;
            value=XDocument.Load(file).Descendants(setting).FirstOrDefault()?.Value;
            break;
        }
        if(string.IsNullOrWhiteSpace(value))throw new InvalidOperationException("Configure "+setting+" in paths.props or its environment variable.");
        return Path.GetFullPath(Path.IsPathRooted(value)?value:Path.Combine(gameRoot,value));
    }
}
