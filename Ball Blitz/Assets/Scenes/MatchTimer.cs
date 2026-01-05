using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class MatchTimer : MonoBehaviour
{
    [Header("Timer")]
    public float matchDurationSeconds = 120f;  
    public TextMeshProUGUI timerText;

    [Header("Lose Condition")]
    public string loseSceneName = "Lose Menu";

    float remaining;
    bool ended = false;

    void Awake()
    {
        DodgerWinManager.MatchEnded = false;
        remaining = matchDurationSeconds;
        UpdateUI();
    }

    void Update()
    {
        if (ended) return;

        remaining -= Time.deltaTime;
        if (remaining <= 0f)
        {
            remaining = 0f;
            UpdateUI();
            EndLose();
            return;
        }

        UpdateUI();
    }

    void UpdateUI()
    {
        if (!timerText) return;

        int totalSeconds = Mathf.CeilToInt(remaining);
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;

        timerText.text = $"{minutes:00}:{seconds:00}";
    }

    void EndLose()
    {
        ended = true;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (DodgerWinManager.MatchEnded) return;
        DodgerWinManager.MatchEnded = true;
        SceneManager.LoadScene(loseSceneName);
    }
}
