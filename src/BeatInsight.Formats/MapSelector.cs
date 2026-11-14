using System;
using System.IO;
using System.Linq;

namespace BeatInsight.Formats;

public sealed class DuplicateMapDeclaration
{
    public string Code=>"duplicate-map-declaration";
    public string Characteristic {get;set;}="";
    public string Difficulty {get;set;}="";
    public string File {get;set;}="";
    public int Occurrences {get;set;}
}

public sealed class MapSelectionResult
{
    public MapCase Case {get;set;}=new MapCase();
    public DuplicateMapDeclaration[] Diagnostics {get;set;}=Array.Empty<DuplicateMapDeclaration>();
}

public static class MapSelector
{
    public static MapSelectionResult Select(string folder,string characteristic,string difficulty,string? file=null)
    {
        var cases=LocalMap.Cases(folder).ToArray();
        var diagnostics=cases.GroupBy(c=>new {c.Characteristic,c.Difficulty,File=LocalMap.SafePath(c.Folder,c.File).ToUpperInvariant()})
            .Where(g=>g.Count()>1).Select(g=>new DuplicateMapDeclaration {Characteristic=g.Key.Characteristic,
                Difficulty=g.Key.Difficulty,File=g.First().File,Occurrences=g.Count()}).ToArray();
        var matching=cases.Where(c=>c.Characteristic==characteristic&&c.Difficulty==difficulty);
        if(file!=null)
        {
            string exact=LocalMap.SafePath(folder,file);
            matching=matching.Where(c=>string.Equals(LocalMap.SafePath(c.Folder,c.File),exact,StringComparison.OrdinalIgnoreCase));
        }
        var files=matching.GroupBy(c=>LocalMap.SafePath(c.Folder,c.File),StringComparer.OrdinalIgnoreCase).ToArray();
        if(files.Length==0)throw new ArgumentException("No matching map declaration for "+characteristic+"/"+difficulty+(file==null?"":" in "+file)+".");
        if(files.Length>1)throw new ArgumentException("Ambiguous map selection for "+characteristic+"/"+difficulty+
            ". Use --file <relative path>. Candidates: "+string.Join(", ",files.Select(g=>g.First().File)));
        // Repeated declarations of one exact file are diagnosed, not confused
        // with the genuinely ambiguous choice between different map files.
        return new MapSelectionResult {Case=files[0].First(),Diagnostics=diagnostics};
    }
}
