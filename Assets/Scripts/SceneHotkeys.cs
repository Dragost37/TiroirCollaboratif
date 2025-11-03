using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-100)]
public class SceneHotkeys : MonoBehaviour
{
    // Assignez ici (dans l’Inspector) les noms EXACTS des scènes (Build Settings)
    [Header("Scènes pour & é \" ' ( -  (AZERTY 1..6)")]
    public string sceneForAmpersand_1 = "SCENARIO2";   // & (Alpha1)
    public string sceneForEAcute_2 = "SCENARIO4";      // é (Alpha2)
    public string sceneForQuote_3 = "SCENARIO5";       // " (Alpha3)
    public string sceneForApostrophe_4 = "SCENARIO6";// ' (Alpha4)
    public string sceneForLeftParen_5 = "HT";   // ( (Alpha5)
    public string sceneForHyphen_6;      // - (Alpha6)

    private static SceneHotkeys _instance;

    // --- Bootstrap automatique au lancement du jeu ---
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance != null) return;
        var go = new GameObject("SceneHotkeys (Auto)");
        _instance = go.AddComponent<SceneHotkeys>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        // AZERTY : ces symboles envoient Alpha1..Alpha6
        if (Input.GetKeyDown(KeyCode.Alpha1)) TryLoad(sceneForAmpersand_1);   // &
        if (Input.GetKeyDown(KeyCode.Alpha2)) TryLoad(sceneForEAcute_2);     // é
        if (Input.GetKeyDown(KeyCode.Alpha3)) TryLoad(sceneForQuote_3);      // "
        if (Input.GetKeyDown(KeyCode.Alpha4)) TryLoad(sceneForApostrophe_4); // '
        if (Input.GetKeyDown(KeyCode.Alpha5)) TryLoad(sceneForLeftParen_5);  // (
        if (Input.GetKeyDown(KeyCode.Alpha6)) TryLoad(sceneForHyphen_6);     // -
    }

    private void TryLoad(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName)) return;

        // Vérifie que la scène est dans Build Settings (par nom)
        if (Application.CanStreamedLevelBeLoaded(sceneName))
        {
            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        }
        else
        {
            Debug.LogWarning($"[SceneHotkeys] La scène \"{sceneName}\" n'est pas trouvée dans Build Settings.");
        }
    }
}
