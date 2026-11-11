using System.Globalization;
using System.Xml;
using BeatInsight.Core;

static class Preview
{
    public static void Write(string file,MenuRenderData graph,DrawColor left,DrawColor right)
    {
        using var writer=XmlWriter.Create(file,new XmlWriterSettings {Indent=true});
        writer.WriteStartElement("svg","http://www.w3.org/2000/svg");writer.WriteAttributeString("width","1000");writer.WriteAttributeString("height","890");writer.WriteAttributeString("viewBox","-3 -6 106 94");
        writer.WriteStartElement("rect");writer.WriteAttributeString("x","-3");writer.WriteAttributeString("y","-6");writer.WriteAttributeString("width","106");writer.WriteAttributeString("height","94");writer.WriteAttributeString("fill","#091e29");writer.WriteEndElement();
        var ready=new MenuRenderData {Average="3.25 / 3.48",Peak="7 / 8",Median="3 / 3.5",Squats="12",
            Left=FirstNoteVisual.From(new MapNote {Direction=6,CutDirectionAngleOffset=12},left),Right=FirstNoteVisual.From(new MapNote {Direction=8,CutDirectionAngleOffset=23},right)};
        Text("Nominal tilt: arrow + dot",0,-2,2.5f);Draw(MenuDrawing.Panel(ready),0);Draw(MenuDrawing.Graph(graph),19);
        ready.Left=FirstNoteVisual.From(new MapNote {Direction=6},left);ready.Right=FirstNoteVisual.From(new MapNote {Direction=8},right);
        Text("Unknown tilt: dotted background",0,39,2.5f);Draw(MenuDrawing.Panel(ready),41);
        Text("Unavailable / error",0,65,2.5f);Draw(MenuDrawing.Panel(new MenuRenderData()),67);
        writer.WriteEndElement();
        void Draw(Drawing drawing,float offset)
        {
            foreach(var t in drawing.Triangles)
            {
                writer.WriteStartElement("polygon");writer.WriteAttributeString("points",Point(t.A)+" "+Point(t.B)+" "+Point(t.C));
                writer.WriteAttributeString("fill",$"rgb({(int)(255*t.Color.R)},{(int)(255*t.Color.G)},{(int)(255*t.Color.B)})");writer.WriteAttributeString("fill-opacity",F(t.Color.A*(t.AlphaA+t.AlphaB+t.AlphaC)/3));writer.WriteEndElement();
            }
            foreach(var label in drawing.Labels)Text(label.Text,label.X,label.Y+offset,label.Size);
            string Point(DrawPoint p)=>F(p.X)+","+F(p.Y+offset);
        }
        void Text(string text,float x,float y,float size)
        {
            writer.WriteStartElement("text");writer.WriteAttributeString("x",F(x));writer.WriteAttributeString("y",F(y));writer.WriteAttributeString("fill","white");writer.WriteAttributeString("font-family","Arial, sans-serif");
            writer.WriteAttributeString("font-size",F(size));writer.WriteAttributeString("font-style","italic");writer.WriteAttributeString("font-weight","bold");writer.WriteAttributeString("dominant-baseline","central");writer.WriteString(text);writer.WriteEndElement();
        }
        string F(float value)=>value.ToString("0.###",CultureInfo.InvariantCulture);
    }
}
