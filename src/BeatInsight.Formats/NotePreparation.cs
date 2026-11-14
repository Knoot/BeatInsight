using System;
using System.Collections.Generic;
using System.Linq;
using BeatInsight.Core;

namespace BeatInsight.Formats;

internal sealed class PreparedSlider
{
    public MapNote Head { get; set; } = new MapNote();
    public float TailTime { get; set; }
    public int TailX { get; set; }
    public int TailY { get; set; }
    public bool Chain { get; set; }
}

// Independent reconstruction of the game's 1 ms row processing. In particular,
// overlap is geometric (not color-dependent), and head operations retain their order.
internal static class NotePreparation
{
    public static MapNote[] Prepare(List<MapNote> notes, List<MapNote> bombs, List<PreparedSlider> arcs, List<PreparedSlider> chains, bool chainsBeforeArcs = false,bool prepareOrientation=true)
    {
        if(prepareOrientation)NoteOrientation.Prepare(notes);
        var sources=new List<object>[] {notes.Cast<object>().ToList(),bombs.Cast<object>().ToList(),arcs.Cast<object>().ToList(),chains.Cast<object>().ToList()};
        if(chainsBeforeArcs){var source=sources[2];sources[2]=sources[3];sources[3]=source;}
        var orderedNotes=new LinkedList<MapNote>();LinkedListNode<MapNote>? lastNote=null;
        void InsertNote(MapNote note)
        {
            var cursor=lastNote;
            if(cursor==null)lastNote=orderedNotes.AddLast(note);
            else if(note.Time>=cursor.Value.Time)
            {
                while(cursor!=null&&note.Time>=cursor.Value.Time)cursor=cursor.Next;
                lastNote=cursor==null?orderedNotes.AddLast(note):orderedNotes.AddBefore(cursor,note);
            }
            else
            {
                // Backward insertion goes before existing equal-time items, as in
                // the game's cursor-based sorted list. A stable sort loses this order.
                while(cursor!=null&&note.Time<=cursor.Value.Time)cursor=cursor.Previous;
                lastNote=cursor==null?orderedNotes.AddFirst(note):orderedNotes.AddAfter(cursor,note);
            }
        }
        var positions=new int[4];var arrival=new long[4];long nextArrival=0;
        for(int i=0;i<4;i++)if(sources[i].Count>0)arrival[i]=nextArrival++;
        float Beat(object item)=>item is MapNote n?n.Beat:((PreparedSlider)item).Head.Beat;
        float Time(object item)=>item is MapNote n?n.Time:((PreparedSlider)item).Head.Time;
        var rowNotes=new List<MapNote>();var rowHeads=new List<PreparedSlider>();
        var rowTails=new List<PreparedSlider>();var pendingTails=new List<PreparedSlider>();
        var chainGameplay=new HashSet<MapNote>();var arcHeads=new HashSet<MapNote>();
        float rowStart=0;
        void FinishRow()
        {
            foreach(var slider in rowHeads)
                foreach(var note in rowNotes)
                    if(note.X==slider.Head.X&&note.Y==slider.Head.Y)
                    {
                        if(slider.Chain){chainGameplay.Add(note);note.ScoringType=4;}
                        else {arcHeads.Add(note);note.ScoringType=2;}
                    }
            foreach(var slider in rowTails)
                foreach(var note in rowNotes)
                    if(note.X==slider.TailX&&note.Y==slider.TailY)
                        note.ScoringType=chainGameplay.Contains(note)?7:arcHeads.Contains(note)?6:3;
            rowNotes.Clear();rowHeads.Clear();rowTails.Clear();
        }
        while(true)
        {
            int best=-1;
            for(int i=0;i<4;i++)if(positions[i]<sources[i].Count)
            {
                if(best<0||Beat(sources[i][positions[i]])<Beat(sources[best][positions[best]])||
                    Beat(sources[i][positions[i]])==Beat(sources[best][positions[best]])&&arrival[i]<arrival[best])best=i;
            }
            if(best<0)break;
            object item=sources[best][positions[best]++];arrival[best]=nextArrival++;
            float time=Time(item);
            if(time>rowStart+.001f)
            {
                FinishRow();rowStart=time;
                pendingTails.RemoveAll(s=>s.TailTime<rowStart-.001f);
                while(pendingTails.Count>0&&Math.Abs(pendingTails[0].TailTime-rowStart)<.001f)
                {rowTails.Add(pendingTails[0]);pendingTails.RemoveAt(0);}
            }
            if(item is MapNote note){rowNotes.Add(note);InsertNote(note);}
            else
            {
                var slider=(PreparedSlider)item;rowHeads.Add(slider);
                int index=pendingTails.FindIndex(s=>s.TailTime>slider.TailTime);
                if(index<0)pendingTails.Add(slider);else pendingTails.Insert(index,slider);
            }
        }
        FinishRow();
        foreach(var chain in chains)
        {
            var head=chain.Head;
            head.CutDirectionAngleOffset=notes.FirstOrDefault(n=>n.Time==head.Time&&n.X==head.X&&n.Y==head.Y&&n.Color==head.Color)?.CutDirectionAngleOffset;
        }
        // reference implementation excludes actual ChainHead/ChainHeadArcTail objects, then creates
        // a separate basic note from every chain. An arc may have overwritten a
        // chain head's scoring type back to ArcHead; that note must NOT be deleted.
        return orderedNotes.Where(n=>n.ScoringType>0&&n.ScoringType!=4&&n.ScoringType!=7).Concat(chains.Select(s=>s.Head))
            .OrderBy(n=>n.Time).ToArray();
    }
}
