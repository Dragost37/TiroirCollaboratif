using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameDataTransfer : MonoBehaviour
{
    public static GameDataTransfer Instance { get; private set; }

    public List<int> JoinedPlayerIDs { get; private set; } = new List<int>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this.gameObject);
        }
        else
        {
            Instance = this;
            DontDestroyOnLoad(this.gameObject);
        }
    }

    public void SetJoinedPlayers(List<int> players)
    {
        JoinedPlayerIDs = players;
    }
}
