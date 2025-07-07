using UnityEngine;
using Cysharp.Threading.Tasks;

public class GameInitiator : MonoBehaviour
{
    private async void Start()
    {
        BindObjects();
        await InitializeObjects();
        await CreateObjects();
        BeginGame();
    }

    private void BindObjects()
    {

    }

    private async UniTask InitializeObjects()
    {

    }

    private async UniTask CreateObjects()
    {

    }

    private void BeginGame()
    {

    }
}
