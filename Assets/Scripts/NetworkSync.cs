//#define USE_NETCODE
#if USE_NETCODE
using Unity.Netcode;
#endif
using UnityEngine;

public class NetworkSync : MonoBehaviour
{
#if USE_NETCODE
    public class SyncedPart : NetworkBehaviour
    {
        private NetworkVariable<Vector3> pos = new(writePerm: NetworkVariableWritePermission.Owner);
        private NetworkVariable<Quaternion> rot = new(writePerm: NetworkVariableWritePermission.Owner);
        void Update(){ if(IsOwner){ pos.Value = transform.position; rot.Value = transform.rotation; } else { transform.SetPositionAndRotation(pos.Value, rot.Value);} }
    }

    [ContextMenu("StartHost")] public void StartHost(){ NetworkManager.Singleton.StartHost(); }
    [ContextMenu("StartClient")] public void StartClient(){ NetworkManager.Singleton.StartClient(); }
#else
    [ContextMenu("StartHost")] public void StartHost(){ Debug.Log("Netcode not enabled – define USE_NETCODE"); }
    [ContextMenu("StartClient")] public void StartClient(){ Debug.Log("Netcode not enabled – define USE_NETCODE"); }
#endif
}
