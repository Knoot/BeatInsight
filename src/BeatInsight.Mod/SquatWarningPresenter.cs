using TMPro;
using UnityEngine;

namespace BeatInsight.Mod;

internal sealed class SquatWarningPresenter:MonoBehaviour
{
    static SquatWarningPresenter? instance;
    GameObject? textObject,left,right;
    Material? material;
    float hideAt;
    bool showing;

    internal static void Show(bool circles)
    {
        if(instance==null)instance=new GameObject("BeatInsight squat warning").AddComponent<SquatWarningPresenter>();
        instance.ShowAlert(circles);
    }
    void ShowAlert(bool circles)
    {
        HideAlert();
        if(circles)
        {
            if(left==null||right==null)CreateCircles();
            if(left==null||right==null)return;
            left.SetActive(true);right.SetActive(true);
        }
        else
        {
            if(textObject==null)CreateText();
            if(textObject==null)return;
            textObject.SetActive(true);
        }
        showing=true;hideAt=Time.time+.75f;
    }
    void CreateText()
    {
        var camera=Camera.main;if(camera==null)return;
        textObject=new GameObject("BeatInsight DUCK");
        textObject.transform.SetParent(camera.transform,false);
        textObject.transform.localPosition=new Vector3(0,-.15f,2.4f);
        textObject.transform.localScale=Vector3.one*.035f;
        var text=textObject.AddComponent<TextMeshPro>();
        text.alignment=TextAlignmentOptions.Center;text.color=new Color(1,.15f,.05f,1);
        text.fontSize=18;text.fontStyle=FontStyles.Bold;text.text="DUCK";
    }
    void CreateCircles()
    {
        var shader=Shader.Find("Unlit/Color")??Shader.Find("Sprites/Default");
        if(shader==null)return;
        try
        {
            if(material==null)material=new Material(shader){name="BeatInsight squat warning",color=Color.red};
            if(left==null)left=Circle(-3.2f);
            if(right==null)right=Circle(3.2f);
        }
        catch{Release();throw;}
    }
    GameObject Circle(float x)
    {
        var circle=GameObject.CreatePrimitive(PrimitiveType.Sphere);
        try
        {
            circle.name="BeatInsight squat indicator";
            circle.transform.SetParent(transform,false);
            circle.transform.position=new Vector3(x,1.6f,7);
            circle.transform.localScale=Vector3.one*.45f;
            var collider=circle.GetComponent<Collider>();if(collider!=null){collider.enabled=false;Destroy(collider);}
            circle.GetComponent<Renderer>().sharedMaterial=material;
            circle.SetActive(false);return circle;
        }
        catch{Destroy(circle);throw;}
    }
    void Update(){if(showing&&Time.time>=hideAt)HideAlert();}
    void HideAlert()
    {
        showing=false;
        if(textObject!=null)textObject.SetActive(false);
        if(left!=null)left.SetActive(false);
        if(right!=null)right.SetActive(false);
    }
    internal static void Hide(){if(instance!=null)instance.HideAlert();}
    internal static void Dispose()
    {
        var previous=instance;instance=null;
        if(previous!=null){previous.Release();Destroy(previous.gameObject);}
    }
    void Release()
    {
        HideAlert();
        if(textObject!=null)Destroy(textObject);textObject=null;
        if(left!=null)Destroy(left);left=null;
        if(right!=null)Destroy(right);right=null;
        if(material!=null)Destroy(material);material=null;
    }
    void OnDisable()=>HideAlert();
    void OnDestroy(){Release();if(instance==this)instance=null;}
}
