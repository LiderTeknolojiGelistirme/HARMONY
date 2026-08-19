using System;
using System.Collections;
using Michsky.MUIP;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;
using Zenject;

public class MagicianSimulator : MonoBehaviour
{
    public static MagicianSimulator instance;

    [Inject] private ExecCommandViaSocket execCommandViaSocket;
    [Inject] private GameConfig gameConfig;
    
    private TMP_Text errorText;
    private Button crButton;

    
    private void Awake()
    {
        instance = this;
        
        // Config'ten değerleri al
        if (gameConfig != null)
        {
            errorText = gameConfig.errorText;
            crButton = gameConfig.crButton;
        }
    }

    public void MagicianSimRun()
    {
        float seconds = Random.Range(15f, 20f);
        StartCoroutine(MagicianSimRun(seconds));
    }
    public IEnumerator MagicianSimRun(float seconds)
    {
        yield return new WaitForSeconds(seconds);

        errorText.gameObject.SetActive(true);
        crButton.interactable = true;
        execCommandViaSocket.Send("kill $(pgrep -f robot1_robot_goal.py)"); // Stop SR Robot
        
    }
    
    // Factory için
    public class Factory : PlaceholderFactory<MagicianSimulator> { }
}
