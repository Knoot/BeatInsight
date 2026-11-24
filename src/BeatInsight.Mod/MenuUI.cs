using System;
using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using BeatInsight.Core;
using BeatInsight.Formats;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using HMUI;
using BeatSaberMarkupLanguage;
using TMPro;

namespace BeatInsight.Mod;

public sealed class MenuUI:MonoBehaviour
{
    static MenuUI? current;
    static bool hierarchyLogged;
    StandardLevelDetailViewController controller=null!;
    StandardLevelDetailView view=null!;
    RectTransform parent=null!,stats=null!,actionButton=null!,buttons=null!,rowContainer=null!;
    MenuMeshGraphic panel=null!,graph=null!;
    ImageView? averageIcon,peakIcon,squatIcon;
    readonly Dictionary<RectTransform,(Vector2 Original,Vector2 Applied)> shifted=new();
    readonly List<RectTransform> layoutScratch=new(64);
    static readonly string[] movedRows={"BeatmapCharacteristic","BeatmapDifficulty","ModifierSelection","ActionButtons"};
    static readonly string[] tailRows={"BeatmapDifficulty","ModifierSelection","ActionButtons"};
    readonly List<HoverHint> hints=new();
    readonly Vector3[] corners=new Vector3[4];
    float lastWidth,lastRowHeight,lastGraphWidth,lastGraphHeight;
    bool reportFooter;
    ColorSchemesSettings? colors;
    OverrideEnvironmentSettings? environments;
    MenuRenderData data=new MenuRenderData();
    MenuVisibility visibility=new();
    internal static void SettingsChanged()
    {
        if(current==null)return;
        current.visibility=Plugin.Config.Visibility();current.Position();current.Render(MenuAnalysis.Current);
    }

    internal static void Enable(){MenuAnalysis.Changed-=Changed;MenuAnalysis.Changed+=Changed;}
    internal static void Disable()
    {MenuAnalysis.Changed-=Changed;try{Hide();}finally{MenuIcons.Clear();}}
    static void Changed(SelectionState<MenuSelection,SelectedMapAnalysis> state)
    {if(current!=null)current.Render(state);}

    internal static void Activate(StandardLevelDetailViewController owner)
    {
        Hide();
        var go=new GameObject("BeatInsight.Menu",typeof(RectTransform));
        try
        {
            var display=go.AddComponent<MenuUI>();current=display;display.controller=owner;
            display.visibility=Plugin.Config.Visibility();
            display.view=(StandardLevelDetailView)AccessTools.Field(typeof(StandardLevelDetailViewController),"_standardLevelDetailView").GetValue(owner);
            display.stats=(RectTransform)((LevelParamsPanel)AccessTools.Field(typeof(StandardLevelDetailView),"_levelParamsPanel").GetValue(display.view)).transform;
            // Stay outside the native stats subtree: other mods clone it.
            display.parent=(RectTransform)display.stats.parent;go.transform.SetParent(display.parent,false);go.layer=display.stats.gameObject.layer;
            // A zero-size origin at the native pivot avoids mixing view-local
            // bounds with a stretched child's center/anchor reference point.
            var root=(RectTransform)go.transform;root.anchorMin=root.anchorMax=display.parent.pivot;
            root.sizeDelta=Vector2.zero;root.anchoredPosition=Vector2.zero;
            go.AddComponent<LayoutElement>().ignoreLayout=true;
            display.actionButton=(RectTransform)display.view.actionButton.transform;
            var player=(PlayerDataModel)AccessTools.Field(typeof(StandardLevelDetailViewController),"_playerDataModel").GetValue(owner);
            display.colors=player.playerData.colorSchemesSettings;display.colors.didChangeOverrideSettingsEvent+=display.PaletteChanged;
            display.environments=player.playerData.overrideEnvironmentSettings;display.environments.didChangeOverrideSettingsEvent+=display.PaletteChanged;
            display.panel=display.Graphic("Statistics",MenuDrawing.PanelHeight);
            display.graph=display.Graphic("Song density",MenuDrawing.GraphHeight);
            // Keep the compact timeline outside native layout groups; its
            // position follows the existing mode/difficulty gap after layout.
            display.buttons=(RectTransform)(display.parent.Find("ActionButtons")??display.actionButton.parent);
            var footer=display.graph.rectTransform;footer.SetParent(display.transform,false);
            footer.anchorMin=footer.anchorMax=new Vector2(.5f,.5f);
            footer.pivot=new Vector2(1,.5f);
            display.graph.gameObject.AddComponent<LayoutElement>().ignoreLayout=true;
            var container=new GameObject("BeatInsight.Rows",typeof(RectTransform),typeof(VerticalLayoutGroup));
            container.transform.SetParent(display.transform,false);container.layer=go.layer;
            display.rowContainer=(RectTransform)container.transform;
            display.rowContainer.pivot=new Vector2(0,1);
            var layout=container.GetComponent<VerticalLayoutGroup>();
            layout.childControlWidth=true;layout.childControlHeight=true;
            layout.childForceExpandWidth=true;layout.childForceExpandHeight=false;
            var nativeText=display.stats.Find("NPS/ValueText").GetComponent<TextMeshProUGUI>();
            foreach(var graphic in new[]{display.panel})
            {
                graphic.transform.SetParent(container.transform,false);
                graphic.gameObject.AddComponent<LayoutElement>();graphic.UseNativeText(nativeText);
            }
            display.averageIcon=display.Icon(display.panel,"saber1",1.6f);
            display.peakIcon=display.Icon(display.panel,"saber2",19.6f);
            display.squatIcon=display.Icon(display.panel,"squat",47.6f);
            display.Region(display.panel,0,18f/70,0,1);
            display.Region(display.panel,18f/70,32f/70,0,1);
            display.Region(display.panel,32f/70,46f/70,0,1);
            display.Region(display.panel,56f/70,61.4f/70,0,1.5f);
            display.Region(display.panel,61.4f/70,68f/70,0,1.5f);
            display.Region(display.panel,46f/70,56f/70,0,1.5f);
            display.Region(display.graph,0,1,4.1f/MenuDrawing.GraphHeight,1);
            display.Region(display.graph,0,1,0,2.65f/MenuDrawing.GraphHeight);
            display.Region(display.graph,0,1,2.65f/MenuDrawing.GraphHeight,4.1f/MenuDrawing.GraphHeight);
            Canvas.ForceUpdateCanvases();display.Position();display.Render(MenuAnalysis.Current);
            Canvas.willRenderCanvases+=display.PositionFooter;
            if(!hierarchyLogged)display.Invoke(nameof(LogNativeLayout),2f);
        }
        catch {Hide();if(go!=null)Destroy(go);throw;}
    }

    internal static void Deactivate(StandardLevelDetailViewController owner)
    {if(current!=null&&ReferenceEquals(current.controller,owner))Hide();}

    static void Hide()
    {
        var old=current;current=null;
        if(old==null)return;
        old.Restore();old.gameObject.SetActive(false);Destroy(old.gameObject);
    }

    MenuMeshGraphic Graphic(string name,float height)
    {
        var go=new GameObject("BeatInsight."+name,typeof(RectTransform),typeof(CanvasRenderer));go.transform.SetParent(transform,false);go.layer=gameObject.layer;
        var rect=(RectTransform)go.transform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.pivot=new Vector2(.5f,1);
        rect.sizeDelta=new Vector2(100,height);return go.AddComponent<MenuMeshGraphic>();
    }

    ImageView Icon(MenuMeshGraphic row,string resource,float x)
    {
        var sprite=MenuIcons.Get(resource);
        var image=Instantiate(stats.Find(resource=="squat"?"ObstaclesCount/Icon":"NPS/Icon").GetComponent<ImageView>(),row.transform,false);
        image.name="BeatInsight.Icon."+resource;image.useScriptableObjectColors=false;
        if(sprite!=null){image.sprite=sprite;image.overrideSprite=sprite;}
        image.material=Utilities.ImageResources.NoGlowMat;
        image.preserveAspect=true;image.raycastTarget=false;
        var rect=image.rectTransform;rect.localScale=Vector3.one;rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(.5f,.5f);
        rect.anchoredPosition=new Vector2(x,-3.3f);rect.sizeDelta=new Vector2(3.2f,4);
        return image;
    }

    void Region(MenuMeshGraphic owner,float left,float right,float top,float bottom)
    {
        var go=new GameObject("BeatInsight.HoverRegion",typeof(RectTransform),typeof(CanvasRenderer));
        go.layer=gameObject.layer;go.transform.SetParent(owner.transform,false);
        var rect=(RectTransform)go.transform;rect.anchorMin=new Vector2(left,1-bottom);rect.anchorMax=new Vector2(right,1-top);
        rect.offsetMin=rect.offsetMax=Vector2.zero;
        var hit=go.AddComponent<ImageView>();hit.sprite=Utilities.ImageResources.BlankSprite;
        hit.material=Utilities.ImageResources.NoGlowMat;hit.color=Color.clear;hit.raycastTarget=true;
        var hint=go.AddComponent<HoverHint>();
        AccessTools.Field(typeof(HoverHint),"_hoverHintController").SetValue(hint,BeatSaberUI.HoverHintController);
        hints.Add(hint);
    }

    Bounds Bounds(RectTransform rect)=>RelativeBounds(rect,parent);
    Bounds RelativeBounds(RectTransform rect,Transform space)
    {
        rect.GetWorldCorners(corners);
        var result=new Bounds(space.InverseTransformPoint(corners[0]),Vector3.zero);
        for(int i=1;i<4;i++)result.Encapsulate(space.InverseTransformPoint(corners[i]));return result;
    }

    void Position()
    {
        if(stats==null||actionButton==null||panel==null||graph==null)return;
        rowContainer.gameObject.SetActive(visibility.HasStatistics);
        if(!visibility.HasStatistics)
        {
            foreach(var item in shifted)if(item.Key!=null&&item.Key.anchoredPosition==item.Value.Applied)item.Key.anchoredPosition=item.Value.Original;
            shifted.Clear();PositionFooter();return;
        }
        float left=Bounds(stats).min.x;
        // The native stats rect includes unused cell width beyond the view's
        // right edge. Use the actual difficulty block as the content boundary.
        var widthReference=parent.Find("BeatmapDifficulty") as RectTransform;
        float right=widthReference!=null?Bounds(widthReference).max.x:parent.rect.xMax;
        float width=Mathf.Max(1,right-left);
        float scale=width/MenuDrawing.Width;
        var nativeRows=stats.parent.Cast<Transform>().Where(t=>t.gameObject.activeInHierarchy&&(t==stats||t.name.StartsWith(stats.name+"(",StringComparison.Ordinal)))
            .OfType<RectTransform>().ToArray();
        var rows=nativeRows.Select(Bounds).OrderByDescending(b=>b.max.y).ToArray();
        float rowHeight=rows.Length>1?rows[0].max.y-rows[1].max.y:4;
        if(rowHeight<=0||rowHeight>6)rowHeight=4;
        // reference implementation renames its clone to CustomParamsPanel. It can appear after
        // activation and its occupied cells can extend outside the root rect.
        var occupied=nativeRows.Concat(stats.parent.Cast<Transform>()
            .Where(t=>t.gameObject.activeInHierarchy&&t.name=="CustomParamsPanel").OfType<RectTransform>());
        float bottom=float.PositiveInfinity;
        foreach(var row in occupied)
        {
            layoutScratch.Clear();row.GetComponentsInChildren(false,layoutScratch);
            foreach(var rect in layoutScratch)bottom=Mathf.Min(bottom,Bounds(rect).min.y);
        }
        layoutScratch.Clear();
        if(float.IsPositiveInfinity(bottom))return;
        float top=bottom+Mathf.Max(0,stats.rect.height-rowHeight);
        rowContainer.anchoredPosition=new Vector2(left,top);
        rowContainer.sizeDelta=new Vector2(width,rowHeight);
        var element=panel.GetComponent<LayoutElement>();element.minHeight=element.preferredHeight=rowHeight;
        element.minWidth=0;element.preferredWidth=width;
        panel.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,width);
        // Reserve real space before mode/difficulty rows rather than drawing on them.
        var characteristic=parent.Find("BeatmapCharacteristic") as RectTransform;
        if(characteristic!=null)
        {
            float originalTop=Bounds(characteristic).max.y;
            if(shifted.TryGetValue(characteristic,out var old)&&characteristic.anchoredPosition==old.Applied)
                originalTop+=old.Original.y-old.Applied.y;
            float delta=Mathf.Max(0,originalTop-(top-rowHeight-2*scale));
            foreach(string name in movedRows)
                if(parent.Find(name) is RectTransform target)
                {
                    Vector2 original=target.anchoredPosition;
                    if(shifted.TryGetValue(target,out var previous)&&original==previous.Applied)original=previous.Original;
                    Vector2 applied=original-new Vector2(0,delta);target.anchoredPosition=applied;shifted[target]=(original,applied);
                }
            // Extensions can reposition the characteristic row independently.
            // Keep the native half-unit gap to difficulty rather than preserving
            // a stale gap from separately captured positions. Move its whole tail.
            if(parent.Find("BeatmapDifficulty") is RectTransform difficulty)
            {
                float correction=Bounds(characteristic).min.y-.5f-Bounds(difficulty).max.y;
                foreach(string name in tailRows)
                    if(parent.Find(name) is RectTransform target&&shifted.TryGetValue(target,out var saved))
                    {
                        var applied=target.anchoredPosition+new Vector2(0,correction);
                        target.anchoredPosition=applied;shifted[target]=(saved.Original,applied);
                    }
            }
        }
        PositionFooter();
        if(Math.Abs(lastWidth-width)>.05f||Math.Abs(lastRowHeight-rowHeight)>.05f)
        {lastWidth=width;lastRowHeight=rowHeight;LayoutRebuilder.ForceRebuildLayoutImmediate(rowContainer);Draw();}
    }

    void LateUpdate()=>Position();

    void PositionFooter()
    {
        if(graph==null||actionButton==null)return;
        if(!visibility.HasTimeline){graph.gameObject.SetActive(false);return;}
        if(parent.Find("BeatmapCharacteristic") is not RectTransform characteristic||
            parent.Find("BeatmapDifficulty") is not RectTransform difficulty)return;
        var mode=Bounds(characteristic);var levels=Bounds(difficulty);
        float gap=mode.min.y-levels.max.y;
        if(gap<=0){graph.gameObject.SetActive(false);return;}
        graph.gameObject.SetActive(true);
        graph.rectTransform.anchoredPosition=new Vector2(levels.max.x,(mode.min.y+levels.max.y)/2);
        var size=new Vector2(Mathf.Max(1,levels.size.x),gap);
        if(graph.rectTransform.sizeDelta!=size)graph.rectTransform.sizeDelta=size;
        if(Math.Abs(lastGraphWidth-size.x)>.01f||Math.Abs(lastGraphHeight-size.y)>.01f)
        {lastGraphWidth=size.x;lastGraphHeight=size.y;ShowGraph();}
        if(reportFooter)
        {
            reportFooter=false;
            Plugin.Log.Info(string.Format(CultureInfo.InvariantCulture,
                "Compact timeline in existing mode/difficulty gap: gap={0:F2}; width={1:F2}",gap,size.x));
        }
    }

    void ShowGraph()
    {
        if(graph==null)return;
        float scale=Mathf.Max(1,graph.rectTransform.rect.width)/MenuDrawing.Width;
        float height=Mathf.Max(.01f,graph.rectTransform.rect.height/scale);
        var drawing=MenuDrawing.CompactGraph(data,height);visibility.Apply(drawing);graph.Show(drawing,height);
        if(hints.Count<9)return;
        // Time labels cannot fit in this existing gap. Keep the density hit
        // region across the band. Separate source zones above the band stay
        // accessible even if the source is empty, unreadable or entirely stale.
        var density=(RectTransform)hints[6].transform;
        density.gameObject.SetActive(visibility.Density);
        density.anchorMin=Vector2.zero;density.anchorMax=Vector2.one;density.offsetMin=density.offsetMax=Vector2.zero;
        for(int source=0;source<2;source++)
        {
            var region=HistoryHoverLayout.Region(visibility.ImportedDeaths,visibility.OwnDeaths,source==1);
            var hint=hints[7+source];hint.gameObject.SetActive(region.Enabled);
            if(!region.Enabled)continue;
            var rect=(RectTransform)hint.transform;
            rect.anchorMin=new Vector2(region.Left,1);
            rect.anchorMax=new Vector2(region.Right,1);
            rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=new Vector2(0,-MenuDrawing.CompactMarkerCenter(height)*scale);
            rect.sizeDelta=new Vector2(0,2*MenuDrawing.CompactMarkerHalfExtent*scale);
        }
    }

    // Prefab layout is serialized, not described by the view's managed fields.
    // Capture it once after other menu extensions have installed their rows.
    void LogNativeLayout()
    {
        if(hierarchyLogged||view==null||!view.gameObject.activeInHierarchy)return;
        hierarchyLogged=true;
        Plugin.Log.Info("Menu layout snapshot begin (native hierarchy; no text contents)");
        int count=0;
        void Visit(Transform node,string path)
        {
            if(node==transform||!node.gameObject.activeInHierarchy||count++>=500)return;
            path=path.Length==0?node.name:path+"/"+node.name;
            if(node is RectTransform rect)
            {
                string components=string.Join(",",node.GetComponents<Component>().Where(c=>c!=null).Select(c=>c.GetType().Name));
                Plugin.Log.Info(string.Format(CultureInfo.InvariantCulture,
                    "MenuLayout {0} active={1} rect=({2:F2},{3:F2},{4:F2},{5:F2}) anchors=({6:F2},{7:F2})-({8:F2},{9:F2}) position=({10:F2},{11:F2}) components=[{12}]",
                    path,node.gameObject.activeInHierarchy,rect.rect.x,rect.rect.y,rect.rect.width,rect.rect.height,
                    rect.anchorMin.x,rect.anchorMin.y,rect.anchorMax.x,rect.anchorMax.y,rect.anchoredPosition.x,rect.anchoredPosition.y,components));
                var layout=node.GetComponent<HorizontalOrVerticalLayoutGroup>();
                if(layout!=null)Plugin.Log.Info($"MenuLayout group enabled={layout.enabled} spacing={layout.spacing.ToString(CultureInfo.InvariantCulture)} padding={layout.padding} controlWidth={layout.childControlWidth} controlHeight={layout.childControlHeight} expandWidth={layout.childForceExpandWidth} expandHeight={layout.childForceExpandHeight}");
            }
            foreach(Transform child in node)Visit(child,path);
        }
        Visit(view.transform,"");
        Plugin.Log.Info("Menu layout snapshot end");
    }
    void PaletteChanged()=>Render(MenuAnalysis.Current);
    static DrawColor ColorValue(Color color)=>new DrawColor(color.r,color.g,color.b,color.a);

    ColorScheme? Palette()
    {
        var key=controller.beatmapKey;var level=controller.beatmapLevel;
        if(level==null||key.beatmapCharacteristic==null)return colors?.GetSelectedColorScheme();
        string name=level.GetEnvironmentName(key.beatmapCharacteristic,key.difficulty);
        var environment=Resources.FindObjectsOfTypeAll<EnvironmentInfoSO>().FirstOrDefault(e=>e.serializedName==name);
        bool overridden=false;
        if(environment!=null&&environments?.overrideEnvironments==true)
        {
            var replacement=environments.GetOverrideEnvironmentInfoForType(environment.environmentType);
            if(replacement!=null){environment=replacement;overridden=true;}
        }
        var defaults=environment==null?colors?.GetSelectedColorScheme():environment.colorScheme.colorScheme;
        if(defaults==null)return colors?.GetOverrideColorScheme();
        return ColorSchemeExtensions.ResolveColorScheme(colors?.GetOverrideColorScheme(),colors?.ShouldOverrideLightshowColors()??false,
            level.GetColorScheme(key.beatmapCharacteristic,key.difficulty),defaults,!overridden);
    }

    void Render(SelectionState<MenuSelection,SelectedMapAnalysis> state)
    {
        data=new MenuRenderData();
        string unavailable=AnalysisAvailabilityHints.Selection(state.Status,state.Message);
        string[] descriptions={
            "Average swings per second (left/right)\nOnly seconds containing swings are included.",
            "Peak swings per second (left/right)",
            "Median swings per second (left/right)\nThe middle of the sorted non-empty second bins; for an even count, the mean of the two middle values.",
            "First note (left hand)\nArrow or dot, including physical tilt. Dotted face: tilt is unknown.\nSideways and upward arrows flash.",
            "First note (right hand)\nArrow or dot, including physical tilt. Dotted face: tilt is unknown.\nSideways and upward arrows flash.",
            "Squat count", "Object density over song time", "Yellow crosses: imported death records", "Red crosses: BeatInsight death records"};
        for(int i=0;i<hints.Count;i++)hints[i].text=descriptions[i]+"\n"+unavailable;
        if(state.Status==SelectionStatus.Ready&&state.Result!=null)
        {
            reportFooter=true;
            var result=state.Result;
            var scheme=Palette();
            var neutral=new DrawColor(.5f,.5f,.5f);
            data=SelectedMapPresentation.Create(result,scheme==null?neutral:ColorValue(scheme.saberAColor),scheme==null?neutral:ColorValue(scheme.saberBColor));
            for(int i=0;i<hints.Count;i++)hints[i].text=descriptions[i];
            int seconds=Mathf.Max(0,(int)result.Duration);
            hints[6].text+="\nSong time: 0:00 – "+(seconds/60)+":"+(seconds%60).ToString("00",CultureInfo.InvariantCulture);
            if(result.SquatError!=null)hints[5].text+="\n"+AnalysisAvailabilityHints.Unavailable("Squat count unavailable.",result.SquatError);
            if(result.NoteError!=null)
            {
                for(int i=0;i<5;i++)hints[i].text+="\n"+AnalysisAvailabilityHints.Unavailable("Note analysis unavailable.",result.NoteError);
                hints[6].text+=result.IndependentDensity?"\nNominal object density; note geometry is not verified.":"\n"+AnalysisAvailabilityHints.Unavailable("Density unavailable.",result.DensityError??result.NoteError);
            }
            if(data.ImportedHistory!=null)hints[7].text=data.ImportedHistory.Tooltip;
            if(data.OwnHistory!=null)hints[8].text=data.OwnHistory.Tooltip;
            Plugin.Log.Debug("History markers: imported="+result.DeathMarkers.Count(m=>m.Source=="legacy"&&m.NormalizedPosition.HasValue)+
                "; own="+result.DeathMarkers.Count(m=>m.Source=="beatinsight"&&m.NormalizedPosition.HasValue)+"; ownStatus="+result.OwnHistoryStatus);
        }
        Draw();
    }

    void Draw()
    {
        if(panel!=null)
        {
            var drawing=MenuDrawing.StatisticsRow(data);
            drawing.Triangles.RemoveAll(t=>averageIcon!=null&&Mathf.Max(t.A.X,Mathf.Max(t.B.X,t.C.X))<8||
                peakIcon!=null&&Mathf.Min(t.A.X,Mathf.Min(t.B.X,t.C.X))>=26&&Mathf.Max(t.A.X,Mathf.Max(t.B.X,t.C.X))<32||
                squatIcon!=null&&Mathf.Min(t.A.X,Mathf.Min(t.B.X,t.C.X))>=65&&Mathf.Max(t.A.X,Mathf.Max(t.B.X,t.C.X))<72);
            float scale=panel.rectTransform.rect.width/MenuDrawing.Width;
            if(scale<=0)return;
            float[] positions={3.7f,21.7f,35.7f,49.7f},widths={13,10.3f,10.3f,6.3f};
            for(int i=0;i<4;i++){drawing.Labels[i].X=positions[i]/scale;drawing.Labels[i].Width=widths[i]/scale;drawing.Labels[i].Text=drawing.Labels[i].Text.Replace(" / ","/");}
            visibility.Apply(drawing);
            foreach(var label in drawing.Labels)
            {
                float measured=panel.MeasureText(label.Text);
                if(measured>0)label.Width=measured/scale;
            }
            var iconBounds=new List<StatisticsBounds>();
            void IncludeIcon(ImageView? icon,DrawGroup group,float originalX)
            {
                if(icon!=null)iconBounds.Add(new StatisticsBounds(group,(originalX-icon.rectTransform.rect.width/2)/scale,(originalX+icon.rectTransform.rect.width/2)/scale));
            }
            IncludeIcon(averageIcon,DrawGroup.Average,1.6f);
            IncludeIcon(peakIcon,DrawGroup.Peak,19.6f);
            IncludeIcon(squatIcon,DrawGroup.Squats,47.6f);
            var layout=new StatisticsLayout(visibility,drawing,iconBounds.ToArray());
            Plugin.Log.Debug(string.Format(CultureInfo.InvariantCulture,
                "Statistics layout: nativeWidth={0:F2}; rowWidth={1:F2}; groups={2}; textWidths=[{3}]",
                stats.rect.width,panel.rectTransform.rect.width,layout.Columns.Count,
                string.Join(",",drawing.Labels.Select(label=>(label.Width*scale).ToString("F2",CultureInfo.InvariantCulture)))));
            layout.Apply(drawing);
            void MoveIcon(ImageView? icon,DrawGroup group,float originalX)
            {
                if(icon==null)return;
                var rect=icon.rectTransform;
                rect.anchoredPosition=new Vector2(originalX+layout.Offset(group)*scale,rect.anchoredPosition.y);
            }
            MoveIcon(averageIcon,DrawGroup.Average,1.6f);
            MoveIcon(peakIcon,DrawGroup.Peak,19.6f);
            MoveIcon(squatIcon,DrawGroup.Squats,47.6f);
            if(averageIcon!=null)averageIcon.gameObject.SetActive(visibility.Average);
            if(peakIcon!=null)peakIcon.gameObject.SetActive(visibility.Peak);
            if(squatIcon!=null)squatIcon.gameObject.SetActive(visibility.Squats);
            if(hints.Count>=6)
            {
                bool[] visible={visibility.Average,visibility.Peak,visibility.Median,visibility.FirstNotes,visibility.FirstNotes,visibility.Squats};
                for(int i=0;i<6;i++)hints[i].gameObject.SetActive(visible[i]);
                void MoveHint(int index,float left,float right)
                {
                    var rect=(RectTransform)hints[index].transform;
                    rect.anchorMin=new Vector2(left/100,rect.anchorMin.y);
                    rect.anchorMax=new Vector2(right/100,rect.anchorMax.y);
                    rect.offsetMin=rect.offsetMax=Vector2.zero;
                }
                foreach(var column in layout.Columns)
                {
                    int index=column.Group switch {DrawGroup.Average=>0,DrawGroup.Peak=>1,DrawGroup.Median=>2,DrawGroup.Squats=>5,_=>3};
                    if(column.Group==DrawGroup.FirstNotes)
                    {
                        float middle=(column.Left+column.Right)/2;
                        MoveHint(3,column.Left,middle);MoveHint(4,middle,column.Right);
                    }
                    else MoveHint(index,column.Left,column.Right);
                }
            }
            panel.Show(drawing,panel.rectTransform.rect.height/scale);
        }
        ShowGraph();
    }

    void Restore()
    {
        Canvas.willRenderCanvases-=PositionFooter;
        // Remove the timeline and its hover regions together.
        if(graph!=null){graph.gameObject.SetActive(false);Destroy(graph.gameObject);graph=null!;}
        foreach(var item in shifted)if(item.Key!=null&&item.Key.anchoredPosition==item.Value.Applied)item.Key.anchoredPosition=item.Value.Original;
        shifted.Clear();
        data=new MenuRenderData();hints.Clear();layoutScratch.Clear();
        if(colors!=null){colors.didChangeOverrideSettingsEvent-=PaletteChanged;colors=null;}
        if(environments!=null){environments.didChangeOverrideSettingsEvent-=PaletteChanged;environments=null;}
    }
    void OnDestroy(){Restore();if(ReferenceEquals(current,this))current=null;}
}
