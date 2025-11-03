using System.Collections;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    [Header("Player Setup")]
    // Assign all your player TorqueInputHandlers here in the Inspector
    public TorqueInputHandler[] players;

    [Header("Game Settings")]
    public float endDelay = 3.0f; // Time to wait after all players finish

    private int playersFinished = 0;
    private bool isGameWon = false;

    // Event invoked when the minigame finishes (all players finished)
    public System.Action OnGameEnded;

    void Start()
    {
        if (players == null || players.Length == 0)
        {
            Debug.LogError("No players assigned to the GameManager!");
            return;
        }

        // Assign this manager to all players
        foreach (var player in players)
        {
            player.gameManager = this;
        }

        StartMinigame();
    }

    public void StartMinigame()
    {
        Debug.Log("Starting minigame...");
        playersFinished = 0;
        isGameWon = false;

        foreach (var player in players)
        {
            player.ResetPlayer();
        }
    }

    // This is called by TorqueInputHandler when a player succeeds
    public void PlayerFinished()
    {
        if (isGameWon) return; // Game already ended

        playersFinished++;

        // Check if all players are done
        if (playersFinished >= players.Length)
        {
            StartCoroutine(GameEndSequence());
        }
    }

    private IEnumerator GameEndSequence()
    {
        isGameWon = true;
        OnGameEnded?.Invoke();
        Debug.Log("All players finished! Game Over.");

        // Wait for the specified delay
        yield return new WaitForSeconds(endDelay);

        // After X seconds, end (reset) the minigame
        Debug.Log("Resetting game.");
        StartMinigame();
    }

    // Called when a player fails (exceeds torque limit)
    public void RestartGame()
    {
        Debug.Log("Game restarted due to player failure.");
        StopAllCoroutines();
        StartMinigame();
    }
}
