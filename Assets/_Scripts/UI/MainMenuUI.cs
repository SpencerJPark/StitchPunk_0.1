using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuUI : MonoBehaviour
{
    [SerializeField] private Button playButton;
    [SerializeField] private Button quitButton;
    [Tooltip("Optional. Opens the settings menu (SettingsMenuUI must exist in the scene).")]
    [SerializeField] private Button settingsButton;

    private void Awake()
    {
        if (settingsButton != null)
        {
            settingsButton.onClick.AddListener(() =>
            {
                if (SettingsMenuUI.Instance != null)
                    SettingsMenuUI.Instance.OpenFromMainMenu();
            });
        }

        playButton.onClick.AddListener(() =>
        {
            SceneManager.LoadScene(1);
        });
        quitButton.onClick.AddListener(() =>
        {
            Application.Quit();
        });
    }
}
