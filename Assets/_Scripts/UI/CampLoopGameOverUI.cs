using Unity.Entities;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class CampLoopGameOverUI : MonoBehaviour
{
    private static readonly Color CreamColor = new Color(0.94f, 0.9f, 0.78f);

    [SerializeField] private UIDocument gameOverDocument;

    private VisualElement panel;
    private EntityQuery gameOverQuery;
    private World queryWorld;

    private void OnEnable()
    {
        if (gameOverDocument == null)
            return;

        panel = new VisualElement();
        panel.style.position = Position.Absolute;
        panel.style.left = 0f;
        panel.style.right = 0f;
        panel.style.top = 0f;
        panel.style.bottom = 0f;
        panel.style.backgroundColor = new Color(0f, 0f, 0f, 0.7f);
        panel.style.flexDirection = FlexDirection.Column;
        panel.style.alignItems = Align.Center;
        panel.style.justifyContent = Justify.Center;
        panel.style.display = DisplayStyle.None;

        Label titleLabel = new Label("Your workstation was destroyed");
        titleLabel.style.fontSize = 48f;
        titleLabel.style.color = CreamColor;
        titleLabel.style.marginBottom = 24f;
        panel.Add(titleLabel);

        Button restartButton = new Button(RestartScene);
        restartButton.text = "Restart";
        restartButton.style.fontSize = 24f;
        panel.Add(restartButton);

        gameOverDocument.rootVisualElement.Add(panel);
    }

    private void OnDisable()
    {
        if (panel != null && panel.parent != null)
            panel.RemoveFromHierarchy();
        panel = null;
    }

    private void LateUpdate()
    {
        if (panel == null || panel.style.display == DisplayStyle.Flex)
            return;

        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return;

        EntityManager entityManager = world.EntityManager;
        if (queryWorld != world)
        {
            queryWorld = world;
            // The singleton is baked disabled, so the query must ignore enabled state to find it.
            gameOverQuery = entityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[] { ComponentType.ReadOnly<CampLoopGameOver>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });
        }

        if (gameOverQuery.IsEmptyIgnoreFilter)
            return;

        // GetSingletonEntity throws on enableable component types; take the first entity instead.
        Unity.Collections.NativeArray<Entity> gameOverEntities = gameOverQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
        Entity gameOverEntity = gameOverEntities[0];
        gameOverEntities.Dispose();
        if (!entityManager.IsComponentEnabled<CampLoopGameOver>(gameOverEntity))
            return;

        panel.style.display = DisplayStyle.Flex;
        UnityEngine.Cursor.visible = true;
        UnityEngine.Cursor.lockState = CursorLockMode.None;
    }

    private static void RestartScene()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.buildIndex >= 0)
        {
            SceneManager.LoadScene(activeScene.buildIndex);
            return;
        }

#if UNITY_EDITOR
        UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
            activeScene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
        SceneManager.LoadScene(activeScene.name);
#endif
    }
}
