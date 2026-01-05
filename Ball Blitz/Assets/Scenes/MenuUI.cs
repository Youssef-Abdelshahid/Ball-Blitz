using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuController : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject htpPanel;

    [Header("Scene Names")]
    [SerializeField] private string arenaSceneName = "Arena";

    private void Awake()
    {
        // Scene starts with Main Panel only
        ShowMain();
    }

    public void Play()
    {
        SceneManager.LoadScene(arenaSceneName);
    }

    public void Quit()
    {
        Application.Quit();
        Debug.Log("Quit pressed (won't close in the editor).");
    }

    public void ShowHowToPlay()
    {
        if (mainPanel) mainPanel.SetActive(false);
        if (htpPanel) htpPanel.SetActive(true);
    }

    public void ShowMain()
    {
        if (htpPanel) htpPanel.SetActive(false);
        if (mainPanel) mainPanel.SetActive(true);
    }
}
