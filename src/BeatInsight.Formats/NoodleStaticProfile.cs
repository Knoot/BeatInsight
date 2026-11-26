using System;
using System.Collections.Generic;
using System.Collections;
using Newtonsoft.Json.Linq;

namespace BeatInsight.Formats;

// Nominal real gameplay geometry; fake notes retain identity for compatible
// analysis and fake walls are omitted from physical squat coverage.
internal static class NoodleStaticProfile
{
    static readonly HashSet<string> Cosmetic=new HashSet<string>(StringComparer.Ordinal) {
        "color","_color","spawnEffect","_disableSpawnEffect","disableDebris",
        "noteJumpMovementSpeed","_noteJumpMovementSpeed","noteJumpStartBeatOffset","_noteJumpStartBeatOffset"};
    static readonly HashSet<string> LegacyDefaultFlags=new HashSet<string>(StringComparer.Ordinal) {"_disableNoteGravity","_disableNoteLook"};
    static readonly HashSet<string> DefaultFlags=new HashSet<string>(StringComparer.Ordinal) {"disableNoteGravity","disableNoteLook","disableBadCutDirection","disableBadCutSpeed","disableBadCutSaberType"};
    static void Unsupported(JToken value,string reason)=>throw new NotSupportedException("Unsupported Noodle Extensions gameplay data: "+reason+" ("+value.Path+")");
    internal static bool IsNeutralMetadataName(string name)=>name=="_isAnchor"||name=="_paul";
    internal static string TrackKey(bool old)=>old?"_track":"track";
    internal static bool IsEmptyTrackValue(object? value)
    {
        if(value==null)return true;
        if(value is JToken token)return token.Type==JTokenType.Null||token is JArray array&&array.Count==0;
        return value is IList list&&list.Count==0;
    }
    internal static void ValidateTrackAssignment(JToken value,bool allowNamed)
    {
        if(IsEmptyTrackValue(value))return;
        if(!allowNamed)Unsupported(value,"named tracks need a validated geometry-neutral context");
        if(value.Type==JTokenType.String&&!string.IsNullOrEmpty((string?)value))return;
        if(value is JArray array&&array.Count>0)
        {
            foreach(var item in array)if(item.Type!=JTokenType.String||string.IsNullOrEmpty((string?)item))Unsupported(item,"invalid static track name");
            return;
        }
        Unsupported(value,"invalid static track assignment");
    }
    internal static bool ValidateNeutralMetadata(string name,JToken value)
    {
        if(!IsNeutralMetadataName(name))return false;
        if(name=="_isAnchor")
        {
            if(value.Type!=JTokenType.Boolean&&value.Type!=JTokenType.Null)Unsupported(value,"invalid _isAnchor metadata");
            return true;
        }
        if(value.Type==JTokenType.Float)
        {
            double number=(double)value;
            if(double.IsNaN(number)||double.IsInfinity(number))Unsupported(value,"non-finite _paul metadata");
        }
        else if(value.Type!=JTokenType.Integer&&value.Type!=JTokenType.Null&&value.Type!=JTokenType.Boolean&&value.Type!=JTokenType.String&&value.Type!=JTokenType.Date)
            Unsupported(value,"structured or invalid _paul metadata");
        return true;
    }
    public static void Validate(JObject root,int major,bool notes,bool walls,float bpm,out HashSet<string> nominalTracks)
    {
        nominalTracks=new HashSet<string>(StringComparer.Ordinal);
        if(!notes&&!walls)return;
        if(major==4)throw new NotSupportedException("Noodle Extensions v4 gameplay has not been validated");
        bool staticTracks=IsEmptyTrackValue(root["environment"])&&IsEmptyTrackValue(root["_environment"]);
        foreach(string key in new[]{"_customData","customData"})
        {
            if(root[key]==null||root[key]!.Type==JTokenType.Null)continue;
            if(root[key] is not JObject custom){Unsupported(root[key]!,"invalid root customData");return;}
            foreach(string events in new[]{"_customEvents","customEvents"})if(custom[events]!=null&&custom[events]!.Type!=JTokenType.Null)
            {
                if(custom[events] is not JArray array){Unsupported(custom[events]!,"invalid custom events");return;}
                if(array.Count>0)
                {
                    bool canonical=key==(major==2?"_customData":"customData")&&events==(major==2?"_customEvents":"customEvents");
                    var relevant=array;
                    bool visual=NoodleVisualEvents.IsVisual(array,major);
                    if(canonical&&major==3&&!visual&&NoodleTrackScope.TrySelect(root,array,notes,walls,out var scoped))
                    {relevant=scoped;visual=NoodleVisualEvents.IsVisual(relevant,major);}
                    if(!canonical)staticTracks=false;
                    else if(!visual)
                    {
                        if(NoodleNominalTracks.TryRead(relevant,major,root,bpm,notes,out var affected))nominalTracks.UnionWith(affected);
                        else staticTracks=false;
                    }
                }
                foreach(var item in array)
                {
                    if(item is not JObject){Unsupported(item,"invalid custom event");return;}
                    if((string?)item[major==2?"_type":"t"]=="AssignPlayerToTrack")Unsupported(item,"player track transformation");
                }
            }
            if(!IsEmptyTrackValue(custom["environment"])||!IsEmptyTrackValue(custom["_environment"]))staticTracks=false;
            foreach(string fake in new[]{"fakeColorNotes","fakeBombNotes","fakeObstacles","fakeBurstSliders"})
            {
                if(fake=="fakeObstacles"?!walls:!notes)continue;
                if(custom[fake]==null||custom[fake]!.Type==JTokenType.Null)continue;
                if(custom[fake] is not JArray fakes){Unsupported(custom[fake]!,"invalid fake array");return;}
                if(fake=="fakeBurstSliders"&&fakes.Count>0)Unsupported(fakes,"fake burst sliders require a dedicated row-processing profile");
            }
        }
        var isolatedVisualTracks=staticTracks?new HashSet<string>(StringComparer.Ordinal):NoodleIsolatedVisualTracks.Read(root,major,notes);
        var collections=new List<string>();
        if(notes)collections.AddRange(major==2?new[]{"_notes","_sliders"}:new[]{"colorNotes","bombNotes","sliders","burstSliders"});
        if(walls)collections.Add(major==2?"_obstacles":"obstacles");
        foreach(string name in collections)
        {
            if(root[name]==null||root[name]!.Type==JTokenType.Null)continue;
            if(root[name] is not JArray items){Unsupported(root[name]!,"invalid gameplay collection");return;}
            foreach(var item in items)
            {
                if(item is not JObject){Unsupported(item,"invalid gameplay object");return;}
                bool fakeObject=NoodleFakeData.IsFake(item,major);
                if(fakeObject&&(name=="sliders"||name=="_sliders"||name=="burstSliders"))Unsupported(item,"fake sliders require a dedicated row-processing profile");
                if(fakeObject&&(name=="obstacles"||name=="_obstacles"))continue;
                bool old=major==2,arc=name=="_sliders"||name=="sliders"||name=="burstSliders";
                bool wall=name=="obstacles"||name=="_obstacles";
                string x=arc&&old?"_headLineIndex":old?"_lineIndex":"x",y=arc&&old?"_headLineLayer":old?"_lineLayer":"y";
                void Coordinate(string field,int max)
                {
                    int value;
                    try{value=(int?)item[field]??0;}catch(Exception e) when(e is FormatException||e is OverflowException||e is InvalidCastException||e is ArgumentException)
                    {throw new NotSupportedException("Invalid Noodle Extensions coordinate: "+item.Path+"."+field,e);}
                    if(value<0||value>max)
                    {
                        string tailX=old?"_tailLineIndex":"tx",tailY=old?"_tailLineLayer":"ty";
                        bool tail=field==tailX||field==tailY;
                        if(arc&&(field==x||field==y||tail))
                        {
                            var replacement=NoodleGeometry.Read(item,major,tail?"tailCoordinates":null);
                            bool hasAxis=(field==x||field==tailX?replacement.X:replacement.Y).HasValue;
                            if(hasAxis&&value>=-1000000&&value<=1000000)return;
                            Unsupported(item,"extended slider base coordinate needs an explicit replacement");
                        }
                        var custom=item[old?"_customData":"customData"] as JObject;
                        var position=custom?[old?"_position":"coordinates"] as JArray;
                        var size=custom?[old?"_scale":"size"] as JArray;
                        int axis=field==x?0:1;
                        bool replaced=position!=null&&position.Count>axis&&position[axis].Type!=JTokenType.Null;
                        bool explicitHeight=axis==0||size!=null&&size.Count>1&&size[1].Type!=JTokenType.Null;
                        var width=item[old?"_width":"w"];var layer=item["y"];var height=item["h"];
                        bool bareLane=wall&&field==x&&item[field]?.Type==JTokenType.Integer&&width?.Type==JTokenType.Integer&&(double)width>=0&&(double)width<=1000000
                            &&(old||(layer==null||layer.Type==JTokenType.Integer)&&(height==null||height.Type==JTokenType.Integer&&(double)height>=0&&(double)height<=1000000));
                        if(!wall||(!replaced&&!bareLane)||!explicitHeight||value < -1000000||value>1000000)
                            Unsupported(item,"extended base coordinates need explicit wall coordinates and height for an extended layer");
                    }
                }
                if(wall)BeatInsight.WallAlert.NoodleWallAnalysis.Read(item,major,allowLength:true);
                Coordinate(x,3);Coordinate(y,2);
                if(name=="burstSliders")
                {
                    Coordinate("c",1);
                    int slices;
                    try
                    {
                        if(item["sc"]?.Type!=JTokenType.Integer)Unsupported(item,"burst slice count must be an integer");
                        slices=(int)item["sc"]!;
                    }
                    catch(Exception e) when(e is FormatException||e is OverflowException||e is InvalidCastException||e is ArgumentException)
                    {throw new NotSupportedException("Invalid Noodle burst slice count",e);}
                    if(slices<1||slices>NoodleBurstPreparation.MaxSliceCount)Unsupported(item,"burst slice count is outside the supported range");
                }
                if(arc){Coordinate(old?"_tailLineIndex":"tx",3);Coordinate(old?"_tailLineLayer":"ty",2);}
                if(name=="colorNotes"||name=="_notes"&&((int?)item["_type"]??0)<2)Coordinate(old?"_cutDirection":"d",8);
                if(arc){Coordinate(old?"_headCutDirection":"d",8);Coordinate(old?"_tailCutDirection":name=="burstSliders"?"d":"tc",8);}
                foreach(string key in new[]{"_customData","customData"})
                {
                if(item[key]==null||item[key]!.Type==JTokenType.Null)continue;
                if(item[key] is not JObject custom){Unsupported(item[key]!,"invalid object customData");return;}
                foreach(var property in custom.Properties())
                {
                    if(property.Name=="_fake"||property.Name=="NE_fake"||property.Name=="fake")
                    {
                        if(property.Name!=(major==2?"_fake":"NE_fake")&&property.Value.Type==JTokenType.Boolean&&(bool)property.Value)Unsupported(property.Value,"ambiguous fake flag for this version");
                        if(property.Value.Type==JTokenType.Null||property.Value.Type==JTokenType.Boolean)continue;
                        Unsupported(property.Value,"fake gameplay object or invalid fake flag");
                    }
                    else if(fakeObject&&(property.Name=="_interactable"||property.Name=="uninteractable"))continue;
                    else if(arc&&name!="burstSliders"&&key==(old?"_customData":"customData")&&property.Name==(old?"_interactable":"uninteractable"))
                    {
                        // Arc body interaction does not propagate to the physical head/tail notes.
                        if(property.Value.Type!=JTokenType.Null&&property.Value.Type!=JTokenType.Boolean)Unsupported(property.Value,"invalid arc interaction flag");
                    }
                    else if(major==3&&key=="customData"&&name=="colorNotes"&&!fakeObject&&((int?)item["c"]??0) is 0 or 1
                        &&root["burstSliders"] is not JArray {Count:>0}&&NoodleV3NoteAliases.Contains(property.Name))
                    {
                        NoodleV3NoteAliases.Validate(property.Name,property.Value);
                    }
                    else if(old&&key=="_customData"&&name=="_notes"&&(((int?)item["_type"]??0)<2||!fakeObject&&(int?)item["_type"]==3)&&property.Name=="_cutDirection")
                    {
                        if(property.Value.Type==JTokenType.Null)continue;
                        if(property.Value.Type!=JTokenType.Integer&&property.Value.Type!=JTokenType.Float)Unsupported(property.Value,"invalid legacy cut direction angle");
                        float angle=(float)property.Value;
                        if(float.IsNaN(angle)||float.IsInfinity(angle)||Math.Abs(angle)>1000000)Unsupported(property.Value,"unsupported legacy cut direction angle");
                    }
                    else if(key==(old?"_customData":"customData")&&property.Name==(old?"_position":"coordinates")&&name!="obstacles"&&name!="_obstacles")
                    {
                        NoodleGeometry.Read(item,major);continue;
                    }
                    else if(key==(old?"_customData":"customData")&&property.Name=="tailCoordinates"&&arc){NoodleGeometry.Read(item,major,"tailCoordinates");continue;}
                    else if(name!="burstSliders"&&key==(old?"_customData":"customData")&&
                        (property.Name==(old?"_dissolve":"dissolve")||property.Name==(old?"_dissolveArrow":"dissolveArrow")))
                    {
                        // Direct opacity fields are not consumed by Noodle object data;
                        // their animated counterparts live inside animation custom data.
                        if(!NoodleVisualEvents.Points(property.Value))Unsupported(property.Value,"invalid direct opacity metadata");
                    }
                    else if(name!="burstSliders"&&key==(old?"_customData":"customData")&&property.Name==(old?"_animation":"animation"))
                    {
                        bool entry=major==3&&!fakeObject&&(
                            name=="colorNotes"&&((int?)item["c"]??0) is 0 or 1&&NoodleVisualEvents.IsNominalNoteEntryAnimation(property.Value,major)
                            ||name=="bombNotes"&&NoodleVisualEvents.IsNominalBombEntryAnimation(property.Value,major));
                        if(!NoodleVisualEvents.IsGeometryNeutralAnimation(property.Value,major,wall)&&!entry)Unsupported(property.Value,"animation changes gameplay geometry or is unvalidated");
                    }
                    else if(name!="burstSliders"&&key==(old?"_customData":"customData")&&property.Name==(old?"animation":"_animation")&&NoodleVisualEvents.IsIdentityScaleAnimation(property.Value,old?3:2))continue;
                    else if(major==3&&name=="colorNotes"&&!fakeObject&&key=="customData"&&property.Name=="size"
                        &&((int?)item["c"]??0) is 0 or 1
                        &&root["burstSliders"] is not JArray {Count:>0})
                    {
                        // Installed note data reads scale, not obstacle-only size. Keep
                        // admission limited to the canonical unit metadata proven here.
                        if(property.Value is not JArray size||size.Count!=3)Unsupported(property.Value,"invalid note size");
                        foreach(var component in (JArray)property.Value)
                            if((component.Type!=JTokenType.Integer&&component.Type!=JTokenType.Float)||(double)component!=1)
                                Unsupported(component,"non-unit or invalid note size");
                    }
                    else if(!wall&&!arc&&key==(old?"_customData":"customData")&&property.Name=="tailCoordinates")
                    {
                        // Only slider data consumes these coordinates at runtime.
                        NoodleGeometry.Read(item,major,"tailCoordinates");continue;
                    }
                    else if(!wall&&!arc&&key==(old?"_customData":"customData")&&property.Name==(old?"_scale":"scale"))
                    {
                        if(property.Value.Type==JTokenType.Null)continue;
                        if(property.Value is not JArray scale||scale.Count>3)Unsupported(property.Value,"invalid note scale");
                        foreach(var component in (JArray)property.Value)
                            if(component.Type!=JTokenType.Null&&((component.Type!=JTokenType.Integer&&component.Type!=JTokenType.Float)||(double)component!=1))
                                Unsupported(component,"non-default or invalid note scale");
                    }
                    else if(wall&&key==(old?"_customData":"customData")&&(property.Name==(old?"_position":"coordinates")||property.Name==(old?"_scale":"size")||property.Name==(old?"_localRotation":"localRotation")||property.Name==(old?"_rotation":"worldRotation")))continue;
                    else if(key==(old?"_customData":"customData")&&property.Name==TrackKey(old))
                    {
                        ValidateTrackAssignment(property.Value,staticTracks||NoodleIsolatedVisualTracks.Contains(property.Value,isolatedVisualTracks));
                        if(NoodleNominalTracks.Assigned(property.Value,nominalTracks)
                            &&(major!=3||fakeObject||!(name=="bombNotes"||name=="colorNotes"&&((int?)item["c"]??0) is 0 or 1)))
                            Unsupported(property.Value,"nominal path geometry is validated only for ordinary real notes and bombs");
                    }
                    else if(key==NoodleMetadataScope.Key(major)&&property.Name==NoodleMetadataScope.Key(major))
                    {
                        NoodleMetadataScope.ValidateNested(property.Value);
                    }
                    else if(ValidateNeutralMetadata(property.Name,property.Value))continue;
                    else if(!wall&&key==(old?"_customData":"customData")&&(old?LegacyDefaultFlags:DefaultFlags).Contains(property.Name))
                    {
                        if(property.Value.Type==JTokenType.Null)continue;
                        if(property.Value.Type!=JTokenType.Boolean)Unsupported(property.Value,"active or invalid "+property.Name);
                        bool ordinary=!fakeObject&&(name=="colorNotes"||name=="_notes")&&((int?)item[old?"_type":"c"]??0) is 0 or 1;
                        bool bomb=major==3&&name=="bombNotes"&&!fakeObject;
                        bool cutFlag=major==3&&ordinary&&property.Name.StartsWith("disableBadCut",StringComparison.Ordinal);
                        if((bool)property.Value&&!cutFlag&&(!(ordinary||bomb)||!NoodleMotionFlags.IsMotionFlag(property.Name,major)))Unsupported(property.Value,"active or invalid "+property.Name);
                    }
                    else if(!Cosmetic.Contains(property.Name))Unsupported(property.Value,property.Name);
                }
                }
            }
        }
    }
}
