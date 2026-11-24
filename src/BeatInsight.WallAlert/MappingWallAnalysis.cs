using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace BeatInsight.WallAlert
{
    // Geometry shared by the menu count and the prepared gameplay warning schedule.
    internal static class MappingWallAnalysis
    {
        // Work in exact thousandths of a lane: adjacent fragments must not
        // acquire tiny gaps from float addition. Beat timing remains float.
        internal static long Position(int value)=>value>=1000?(long)value-1000:value<=-1000?(long)value+1000:(long)value*1000;
        internal static long Layer(int value)=>value>=1000||value<=-1000?(long)value-1000:(long)value*1000;
        internal static long Size(int value)=>Position(value);
        internal static (int Layer,int Height) LegacyShape(int type)
        {
            if(type==0)return (0,5);
            if(type==1)return (2,3);
            if(type<1000||type>4005000)throw new NotSupportedException("Unsupported Mapping Extensions obstacle type");
            int packed=type-4001;
            int height=type>=4001?packed/1000:type-1000;
            int start=type>=4001?packed%1000:0;
            return ((int)(start/750f*5*1000+1334),(int)(height/1000f*5*1000+1000));
        }
        internal static WallAnalysis Analyze(string json,float bpm)
        {
            var root=JObject.Parse(json);bool old=root["_obstacles"]!=null;
            var walls=new List<SquatWallData>();
            foreach(var item in (root["_obstacles"]??root["obstacles"]??new JArray()))
            {
                JToken data=item;
                if(root["obstaclesData"] is JArray array)
                {
                    int index=(int?)item["i"]??0;
                    if(index<0||index>=array.Count||array[index] is not JObject)throw new NotSupportedException("Invalid Mapping Extensions obstacle reference");
                    data=array[index];
                }
                int x=(int?)(data[old?"_lineIndex":"x"])??0;
                int width=(int?)(data[old?"_width":"w"])??0;
                int layer=(int?)data["y"]??0,height=(int?)data["h"]??0;
                if(old)(layer,height)=LegacyShape((int?)data["_type"]??0);
                float beat=(float?)item[old?"_time":"b"]??0,duration=(float?)data[old?"_duration":"d"]??0;
                walls.Add(new SquatWallData(beat,beat,beat+duration,x,layer,width,height));
            }
            var times=new List<string>();
            foreach(int index in MappingSquatSchedule.Build(walls))
            {
                var time=TimeSpan.FromSeconds(Math.Max(0,walls[index].Beat)*60/(bpm>0?bpm:120));
                times.Add($"{(int)time.TotalMinutes:00}:{time.Seconds:00}");
            }
            string version=(string)(root["_version"]??root["version"])??"2";
            return new WallAnalysis("v"+version.Split('.')[0],times);
        }
    }
}
