using UnityEngine;

[RequireComponent(typeof(Collider))]
public class PartController : MonoBehaviour
{
    public string compatibleSnapTag;
    public float snapDistance = 0.08f;
    public float snapAngle = 15f;

    private bool _dragging; private int _fingerId=-1; private Vector3 _grabOffset; private Camera _cam;

    void Start(){ _cam = Camera.main; var mt=MultiTouchManager.Instance; mt.OnTouchBegan+=B; mt.OnTouchMoved+=M; mt.OnTouchEnded+=E; }

    void B(MultiTouchManager.TouchEvt e)
    {
        if(_dragging) return; var ray = _cam.ScreenPointToRay(e.position);
        if(Physics.Raycast(ray, out var hit) && hit.collider!=null && hit.collider.gameObject==gameObject)
        { _dragging=true; _fingerId=e.fingerId; var wp = hit.point; _grabOffset = transform.position - wp; }
    }

    void M(MultiTouchManager.TouchEvt e)
    {
        if(!_dragging || e.fingerId!=_fingerId) return; var plane = new Plane(Vector3.up, Vector3.zero); var ray=_cam.ScreenPointToRay(e.position);
        if(plane.Raycast(ray, out var enter))
        { var wp = ray.GetPoint(enter); transform.position = wp + _grabOffset; }
    }

    void E(MultiTouchManager.TouchEvt e)
    {
        if(!_dragging || e.fingerId!=_fingerId) return; _dragging=false; _fingerId=-1; TrySnap();
    }

    void TrySnap()
    {
        var snaps = GameObject.FindGameObjectsWithTag("SnapPoint");
        SnapPoint best=null; float bestDist=float.MaxValue;
        foreach(var go in snaps){ var sp=go.GetComponent<SnapPoint>(); if(sp && sp.snapTag==compatibleSnapTag)
            { var d=Vector3.Distance(transform.position, sp.transform.position); if(d<bestDist){ best=sp; bestDist=d; } } }
        if(best!=null && bestDist<=snapDistance)
        {
            var ang = Quaternion.Angle(transform.rotation, best.transform.rotation);
            if(ang<=snapAngle){ transform.position = best.transform.position; transform.rotation = best.transform.rotation; best.OnSnapped(this); }
        }
    }
}

public class SnapPoint : MonoBehaviour
{
    public string snapTag; public bool occupied; public void OnSnapped(PartController part){ occupied=true; /* TODO: feedback + notify AssemblyManager */ }
}
