using UnityEngine;
using UnityEngine.SceneManagement;

public class DodgerWinManager : MonoBehaviour
{
    [Header("Win condition")]
    [Tooltip("Set to 3 (or any number). When remaining dodgers hit 0, we load nextSceneName.")]
    public int totalDodgers = 3;

    [Tooltip("Scene to load when all dodgers are eliminated.")]
    public string nextSceneName = "Win Menu";
    public static bool MatchEnded;

    int remaining;

    void Awake()
    {
        remaining = totalDodgers;
    }

    public void OnDodgerEliminated()
    {
        remaining = Mathf.Max(0, remaining - 1);

        if (remaining == 0)
        {
            MatchEnded = true;
            SceneManager.LoadScene(nextSceneName);
        }
    }
}
