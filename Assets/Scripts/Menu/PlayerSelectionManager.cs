using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

public class PlayerSelectionManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private Button middleButton;
    [SerializeField] private TextMeshProUGUI buttonPlusIcon;
    [SerializeField] private Image buttonDiceIcon;
    [SerializeField] private Button[] playerButtons = new Button[4];

    [Header("Text Settings")]
    [SerializeField] private string initialText = "Tiroir Collaboratif • Tiroir Collaboratif •";
    [SerializeField] private string playText = "Assembleur ! Assemblement • Assembleur ! Assemblement •";
    [SerializeField] private string waitingText = "Sélection des assembleurs • Sélection des assembleurs •";

    [Header("Button Colors")]
    [SerializeField] private Color defaultButtonColor = Color.white;
    [SerializeField] private Color joinedButtonColor = Color.green;
    [SerializeField] private Color disabledButtonColor = Color.gray;

    private List<bool> playerJoinedStates = new List<bool>(4);
    private bool isInPlayerSelectionMode = false;

    private void Start()
    {
        InitializePlayerSelection();
    }

    private void InitializePlayerSelection()
    {
        for (int i = 0; i < 4; i++)
        {
            playerJoinedStates.Add(false);
        }

        titleText.text = initialText;
        SetPlayerButtonsVisible(false);
        middleButton.gameObject.SetActive(true);
        buttonDiceIcon.gameObject.SetActive(false);

        middleButton.onClick.AddListener(OnMiddleButtonPressed);

        for (int i = 0; i < playerButtons.Length; i++)
        {
            int playerIndex = i;
            playerButtons[i].onClick.AddListener(() => OnPlayerButtonPressed(playerIndex));
        }

        UpdateMiddleButtonState();
    }

    private void OnMiddleButtonPressed()
    {
        if (!isInPlayerSelectionMode)
        {
            StartPlayerSelectionMode();
        }
        else
        {
            StartGame();
        }
    }

    private void StartPlayerSelectionMode()
    {
        isInPlayerSelectionMode = true;
        titleText.text = waitingText;
        SetPlayerButtonsVisible(true);
        middleButton.interactable = false;
        buttonDiceIcon.gameObject.SetActive(true);
        buttonPlusIcon.gameObject.SetActive(false);
        UpdateMiddleButtonColor();
    }

    private void OnPlayerButtonPressed(int playerIndex)
    {
        if (!isInPlayerSelectionMode) return;

        playerJoinedStates[playerIndex] = !playerJoinedStates[playerIndex];

        UpdatePlayerButtonAppearance(playerIndex);

        UpdateMiddleButtonState();
        UpdateTitleBasedOnSelection();
    }

    private void UpdatePlayerButtonAppearance(int playerIndex)
    {
        ColorBlock colors = playerButtons[playerIndex].colors;

        if (playerJoinedStates[playerIndex])
        {
            colors.normalColor = joinedButtonColor;
            colors.selectedColor = joinedButtonColor;
            playerButtons[playerIndex].transform.rotation = Quaternion.Euler(0, 0, 45);
        }
        else
        {
            colors.normalColor = defaultButtonColor;
            colors.selectedColor = defaultButtonColor;
            playerButtons[playerIndex].transform.rotation = Quaternion.Euler(0, 0, 0);
        }

        playerButtons[playerIndex].colors = colors;
    }

    private void UpdateMiddleButtonState()
    {
        if (!isInPlayerSelectionMode)
        {
            middleButton.interactable = true;
            return;
        }

        bool anyPlayerJoined = false;
        foreach (bool joined in playerJoinedStates)
        {
            if (joined)
            {
                anyPlayerJoined = true;
                break;
            }
        }

        middleButton.interactable = anyPlayerJoined;
        UpdateMiddleButtonColor();
    }

    private void UpdateMiddleButtonColor()
    {
        ColorBlock colors = middleButton.colors;

        if (middleButton.interactable)
        {
            colors.normalColor = defaultButtonColor;
        }
        else
        {
            colors.normalColor = disabledButtonColor;
        }

        middleButton.colors = colors;
    }

    private void UpdateTitleBasedOnSelection()
    {
        bool anyPlayerJoined = false;
        foreach (bool joined in playerJoinedStates)
        {
            if (joined)
            {
                anyPlayerJoined = true;
                break;
            }
        }

        if (anyPlayerJoined)
        {
            titleText.text = playText;
        }
        else
        {
            titleText.text = waitingText;
        }
    }

    private void SetPlayerButtonsVisible(bool visible)
    {
        foreach (Button button in playerButtons)
        {
            button.gameObject.SetActive(visible);
        }
    }

    private void StartGame()
    {
        List<int> joinedPlayers = new List<int>();
        for (int i = 0; i < playerJoinedStates.Count; i++)
        {
            if (playerJoinedStates[i])
            {
                joinedPlayers.Add(i + 1);
            }
        }

        Debug.Log($"Starting game with {joinedPlayers.Count} players: {string.Join(", ", joinedPlayers)}");

        if (GameDataTransfer.Instance != null)
        {
            GameDataTransfer.Instance.SetJoinedPlayers(joinedPlayers);
        }
        else
        {
            Debug.LogError("GameDataTransfer n'a pas été trouvé. Les données des joueurs ne seront pas transférées !");
        }

        SceneManager.LoadScene("MAIN", LoadSceneMode.Single);
    }

    public List<int> GetJoinedPlayers()
    {
        List<int> joinedPlayers = new List<int>();
        for (int i = 0; i < playerJoinedStates.Count; i++)
        {
            if (playerJoinedStates[i])
            {
                joinedPlayers.Add(i + 1);
            }
        }
        return joinedPlayers;
    }

    public void ResetPlayerSelection()
    {
        isInPlayerSelectionMode = false;
        for (int i = 0; i < playerJoinedStates.Count; i++)
        {
            playerJoinedStates[i] = false;
            UpdatePlayerButtonAppearance(i);
        }

        titleText.text = initialText;
        SetPlayerButtonsVisible(false);
        middleButton.gameObject.SetActive(true);
        middleButton.interactable = true;
        UpdateMiddleButtonColor();
    }

    void FixedUpdate()
    {
        titleText.transform.rotation = Quaternion.Euler(0, 0, Time.time * 10);

        if (!isInPlayerSelectionMode)
        {
            titleText.transform.localScale = Vector3.one * (0.9f + Mathf.PingPong(Time.time * 0.2f, 0.2f));
        }
        else
        {
            titleText.transform.localScale = Vector3.one;
        }
    }
}
