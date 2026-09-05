using DotsAnimationToolkit;
using Unity.Cinemachine;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Reads the toolkit's <see cref="CutsceneCameraPose"/> singleton each LateUpdate and drives
/// CameraManager's dedicated cutscene vcam (no Follow/LookAt — this bridge owns its transform
/// entirely). isDriven rising edge enters the cutscene camera; the falling edge restores whatever
/// camera was active before. Hard cuts need nothing extra: moving one vcam is instant, and the
/// brain only blends between vcams — entry/exit blends are the brain's own default.
/// </summary>
public class CutsceneCameraBridge : MonoBehaviour
{
    private EntityManager entityManager;
    private EntityQuery cameraPoseQuery;
    private bool worldReady;
    private bool wasDriven;

    private void LateUpdate()
    {
        if (!EnsureWorldReady())
            return;

        if (cameraPoseQuery.CalculateEntityCount() != 1)
            return;

        CutsceneCameraPose pose = cameraPoseQuery.GetSingleton<CutsceneCameraPose>();

        if (pose.isDriven && !wasDriven)
            CameraManager.Instance.EnterCutscene();
        else if (!pose.isDriven && wasDriven)
            CameraManager.Instance.ExitCutscene();

        wasDriven = pose.isDriven;

        if (!pose.isDriven)
            return;

        CinemachineCamera cutsceneCam = CameraManager.Instance.CutsceneCamera;
        if (cutsceneCam == null)
            return;

        Vector3 position = new Vector3(pose.position.x, pose.position.y, pose.position.z);
        Quaternion rotation = new Quaternion(
            pose.rotation.value.x, pose.rotation.value.y, pose.rotation.value.z, pose.rotation.value.w);

        cutsceneCam.transform.SetPositionAndRotation(position, rotation);
        cutsceneCam.Lens.FieldOfView = pose.fieldOfView;
        // Open question (spec §3.6/§7): if the gameplay cameras turn out to be orthographic,
        // this also needs cutsceneCam.Lens.OrthographicSize from an authored value — unverified
        // until the owner checkpoint.
    }

    private bool EnsureWorldReady()
    {
        if (worldReady) return true;

        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return false;

        entityManager = world.EntityManager;
        cameraPoseQuery = entityManager.CreateEntityQuery(typeof(CutsceneCameraPose));
        worldReady = true;
        return true;
    }
}
