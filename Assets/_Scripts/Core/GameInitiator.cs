using UnityEngine;
using Cysharp.Threading.Tasks;

public class GameInitiator : MonoBehaviour
{
    [SerializeField] 
    
    private async void Start()
    {
        BindObjects();
        await InitializeObjects();
        await CreateObjects();
        PrepareGame();
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

    private void PrepareGame()
    {

    }

    private void BeginGame()
    {

    }
}
