using System.Collections.Generic;
using BeatInsight.Core;
using BeatSaberMarkupLanguage;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using HMUI;

namespace BeatInsight.Mod;

public sealed class MenuMeshGraphic:MaskableGraphic
{
    Drawing drawing=new Drawing();
    readonly List<TextMeshProUGUI> labels=new List<TextMeshProUGUI>();
    float drawingHeight;
    bool blinking;
    TextMeshProUGUI? template;
    TextMeshProUGUI? measurement;
    readonly CurvedCanvasSettingsHelper curvature=new();
    protected override void OnEnable()
    {
        base.OnEnable();curvature.Reset();material=Utilities.ImageResources.NoGlowMat;
    }
    protected override void OnTransformParentChanged(){base.OnTransformParentChanged();curvature.Reset();}
    protected override void OnDestroy()
    {
        drawing=new Drawing();labels.Clear();template=null;measurement=null;curvature.Reset();
        base.OnDestroy();
    }
    public void UseNativeText(TextMeshProUGUI value)=>template=value;
    float NativeFontSize=>template==null?0:template.enableAutoSizing?template.fontSizeMax:template.fontSize;
    public float MeasureText(string value)
    {
        if(template==null)return 0;
        if(measurement==null)
        {
            measurement=Instantiate(template,rectTransform,false);
            measurement.name="BeatInsight.TextMeasurement";
            measurement.enabled=false;measurement.raycastTarget=false;
            measurement.enableAutoSizing=false;measurement.enableWordWrapping=false;
        }
        measurement.fontSize=measurement.fontSizeMin=measurement.fontSizeMax=NativeFontSize;
        return measurement.GetPreferredValues(value).x;
    }
    public void Show(Drawing value,float height)
    {
        drawing=value;drawingHeight=height;raycastTarget=false;
        blinking=false;foreach(var triangle in drawing.Triangles)blinking|=triangle.Blink;
        while(labels.Count<drawing.Labels.Count)
        {
            var text=template==null?BeatSaberUI.CreateText(rectTransform,"",Vector2.zero):Instantiate(template,rectTransform,false);
            text.name="BeatInsight.Value";text.raycastTarget=false;text.enableWordWrapping=false;
            text.alignment=TextAlignmentOptions.MidlineLeft;text.enableAutoSizing=template==null;
            text.rectTransform.localScale=Vector3.one;
            labels.Add(text);
        }
        for(int i=0;i<labels.Count;i++)
        {
            var text=labels[i];text.gameObject.SetActive(i<drawing.Labels.Count);
            if(i>=drawing.Labels.Count)continue;
            var item=drawing.Labels[i];var r=text.rectTransform;
            r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,.5f);
            float sx=rectTransform.rect.width/MenuDrawing.Width,sy=rectTransform.rect.height/drawingHeight;
            r.anchoredPosition=new Vector2(item.X*sx,-item.Y*sy);r.sizeDelta=new Vector2(item.Width*sx,5*sy);
            float size=template==null?item.Size*sx:NativeFontSize;
            text.text=item.Text;text.fontSize=text.fontSizeMax=size;text.fontSizeMin=size;
            text.color=template==null?Color.white:template.color;
        }
        SetVerticesDirty();
    }
    void Update(){if(blinking)SetVerticesDirty();}
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();if(drawingHeight<=0)return;
        var rect=rectTransform.rect;
        var settings=curvature.GetCurvedCanvasSettings(canvas);
        float radius=settings==null?0:settings.radius;
        void Vertex(Vector3 point,Color value)
        {
            var vertex=UIVertex.simpleVert;vertex.position=point;vertex.color=value;
            vertex.uv0=new Vector2(.5f,.5f);vertex.uv2=new Vector2(radius,0);
            vh.AddVert(vertex);
        }
        Vector3 Point(DrawPoint p)=>new Vector3(rect.xMin+p.X/MenuDrawing.Width*rect.width,rect.yMax-p.Y/drawingHeight*rect.height,0);
        foreach(var triangle in drawing.Triangles)
        {
            int start=vh.currentVertCount;var c=triangle.Color;var color=new Color(c.R,c.G,c.B,c.A);
            if(template!=null&&c.R==1&&c.G==1&&c.B==1)color=template.color;
            float pulse=triangle.Blink ? .3f+.7f*(.5f+.5f*Mathf.Cos(Time.unscaledTime*2*Mathf.PI)) : 1;
            Color Opacity(float alpha)=>new Color(color.r,color.g,color.b,color.a*alpha*pulse);
            Vertex(Point(triangle.A),Opacity(triangle.AlphaA));Vertex(Point(triangle.B),Opacity(triangle.AlphaB));Vertex(Point(triangle.C),Opacity(triangle.AlphaC));
            vh.AddTriangle(start,start+1,start+2);
        }
    }
}
