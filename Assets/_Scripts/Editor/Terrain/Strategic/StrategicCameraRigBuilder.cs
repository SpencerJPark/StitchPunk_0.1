#if UNITY_EDITOR
using System.Reflection;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class StrategicCameraRigBuilder
{
    private const string ProfileFolder = "Assets/Terrain/Profiles";
    private const string ZoomProfilePath = ProfileFolder + "/StrategicZoomProfile.asset";
    private const string LookProfilePath = ProfileFolder + "/StrategicLook.asset";
    private const string DepthOfFieldProfilePath = ProfileFolder + "/StrategicDoF.asset";
    private const float StrategicPitchDegrees = 40f;
    private const float StrategicFieldOfView = 22f;
    // The reference frame shows roughly 600 m of ground: a long lens far enough back that the strategic layer, not just the detail ring, fills the view.
    private const float StrategicFarClipMetres = 4000f;
    private const float MinimumStrategicDistance = 700f;

    [MenuItem("Stitch Punk/Terrain/Create Strategic Camera Rig")]
    public static void CreateStrategicCameraRig()
    {
        CameraManager cameraManager = Object.FindFirstObjectByType<CameraManager>();
        if (cameraManager == null)
        {
            Debug.LogError("[StrategicRig] No CameraManager in the open scene.");
            return;
        }

        SerializedObject managerObject = new SerializedObject(cameraManager);
        CinemachineCamera caravanCamera = ResolveCaravanCamera(managerObject);
        if (caravanCamera == null)
        {
            Debug.LogError("[StrategicRig] No vehicle or player CinemachineCamera found to build from.");
            return;
        }

        CinemachineMixingCamera mixingCamera = FindOrCreate<CinemachineMixingCamera>("CaravanZoomMix", caravanCamera.transform.parent);
        if (caravanCamera.transform.parent != mixingCamera.transform)
            caravanCamera.transform.SetParent(mixingCamera.transform, true);

        CinemachineCamera strategicCamera = FindOrCreate<CinemachineCamera>("StrategicCamera", mixingCamera.transform);
        ConfigureStrategicCamera(strategicCamera, caravanCamera);

        // Mixing weights follow hierarchy order: caravan first, strategic second.
        caravanCamera.transform.SetSiblingIndex(0);
        strategicCamera.transform.SetSiblingIndex(1);

        EnsureFolder();
        StrategicZoomProfile zoomProfile = LoadOrCreate(ZoomProfilePath, StrategicZoomProfile.CreateDefault);
        VolumeProfile lookProfile = LoadOrCreate(LookProfilePath, CreateLookProfile);
        VolumeProfile depthOfFieldProfile = LoadOrCreate(DepthOfFieldProfilePath, CreateDepthOfFieldProfile);

        CinemachineVolumeSettings volumeSettings = GetOrAdd<CinemachineVolumeSettings>(strategicCamera.gameObject);
        volumeSettings.Profile = lookProfile;
        EditorUtility.SetDirty(volumeSettings);

        Volume depthOfFieldVolume = FindOrCreate<Volume>("StrategicDepthOfField", null);
        depthOfFieldVolume.isGlobal = true;
        depthOfFieldVolume.sharedProfile = depthOfFieldProfile;
        depthOfFieldVolume.enabled = false;

        GameObject rigObject = FindOrCreateObject("StrategicZoomRig", null);
        StrategicViewToggle toggle = GetOrAdd<StrategicViewToggle>(rigObject);
        StrategicRigDriver driver = GetOrAdd<StrategicRigDriver>(rigObject);
        SetReference(toggle, "zoomProfile", zoomProfile);
        SetReference(driver, "mixingCamera", mixingCamera);
        SetReference(driver, "caravanCamera", caravanCamera);
        SetReference(driver, "strategicCamera", strategicCamera);
        SetReference(driver, "zoomProfile", zoomProfile);
        SetReference(driver, "depthOfFieldVolume", depthOfFieldVolume);
        SetReference(driver, "focusTarget", caravanCamera.Follow);

        ReportCameraManagerField(managerObject, mixingCamera);
        EditorSceneManager.MarkSceneDirty(rigObject.scene);
    }

    private static CinemachineCamera ResolveCaravanCamera(SerializedObject managerObject)
    {
        Object vehicleValue = managerObject.FindProperty("vehicleCam").objectReferenceValue;
        CinemachineCamera caravanCamera = vehicleValue as CinemachineCamera;
        if (caravanCamera == null && vehicleValue is CinemachineMixingCamera existingMix)
            caravanCamera = existingMix.GetComponentInChildren<CinemachineCamera>(true);
        if (caravanCamera != null)
            return caravanCamera;

        GameObject existingCaravan = GameObject.Find("CaravanCamera");
        if (existingCaravan != null && existingCaravan.GetComponent<CinemachineCamera>() != null)
            return existingCaravan.GetComponent<CinemachineCamera>();

        CinemachineCamera playerCamera = managerObject.FindProperty("playerCam").objectReferenceValue as CinemachineCamera;
        if (playerCamera == null)
            return null;
        GameObject duplicate = Object.Instantiate(playerCamera.gameObject, playerCamera.transform.parent);
        duplicate.name = "CaravanCamera";
        return duplicate.GetComponent<CinemachineCamera>();
    }

    private static void ConfigureStrategicCamera(CinemachineCamera strategicCamera, CinemachineCamera caravanCamera)
    {
        strategicCamera.Follow = caravanCamera.Follow;
        strategicCamera.LookAt = caravanCamera.LookAt;
        LensSettings lens = strategicCamera.Lens;
        lens.FieldOfView = StrategicFieldOfView;
        lens.FarClipPlane = StrategicFarClipMetres;
        strategicCamera.Lens = lens;

        float distance = MinimumStrategicDistance;
        CinemachineThirdPersonFollow thirdPerson = caravanCamera.GetComponent<CinemachineThirdPersonFollow>();
        if (thirdPerson != null)
            distance = Mathf.Max(distance, thirdPerson.CameraDistance * 6f);
        CinemachinePositionComposer composer = caravanCamera.GetComponent<CinemachinePositionComposer>();
        if (composer != null)
            distance = Mathf.Max(distance, composer.CameraDistance * 3f);

        float yaw = caravanCamera.transform.eulerAngles.y;
        strategicCamera.transform.rotation = Quaternion.Euler(StrategicPitchDegrees, yaw, 0f);

        CinemachineFollow follow = GetOrAdd<CinemachineFollow>(strategicCamera.gameObject);
        Quaternion offsetRotation = Quaternion.Euler(StrategicPitchDegrees, yaw, 0f);
        follow.FollowOffset = offsetRotation * Vector3.back * distance;
        follow.TrackerSettings.BindingMode = Unity.Cinemachine.TargetTracking.BindingMode.WorldSpace;
        follow.TrackerSettings.RotationDamping = Vector3.zero;
        EditorUtility.SetDirty(strategicCamera);
    }

    private static VolumeProfile CreateLookProfile()
    {
        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        ColorAdjustments colorAdjustments = profile.Add<ColorAdjustments>(true);
        colorAdjustments.colorFilter.Override(new Color(1f, 0.9f, 0.75f));
        colorAdjustments.saturation.Override(-15f);
        colorAdjustments.contrast.Override(10f);
        Vignette vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(0.35f);
        return profile;
    }

    private static VolumeProfile CreateDepthOfFieldProfile()
    {
        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        DepthOfField depthOfField = profile.Add<DepthOfField>(true);
        depthOfField.mode.Override(DepthOfFieldMode.Gaussian);
        depthOfField.gaussianStart.Override(0f);
        depthOfField.active = false;
        return profile;
    }

    private static void ReportCameraManagerField(SerializedObject managerObject, CinemachineMixingCamera mixingCamera)
    {
        FieldInfo vehicleField = typeof(CameraManager).GetField("vehicleCam", BindingFlags.Instance | BindingFlags.NonPublic);
        if (vehicleField != null && vehicleField.FieldType.IsAssignableFrom(typeof(CinemachineMixingCamera)))
        {
            managerObject.FindProperty("vehicleCam").objectReferenceValue = mixingCamera;
            managerObject.ApplyModifiedProperties();
            Debug.Log("[StrategicRig] CameraManager.vehicleCam now points at CaravanZoomMix.");
            return;
        }
        Debug.Log("[StrategicRig] Point CameraManager.vehicleCam (Vehicle slot) at CaravanZoomMix once the field is widened to CinemachineVirtualCameraBase. Live camera left unchanged.");
    }

    private static void SetReference(Object target, string fieldName, Object value)
    {
        SerializedObject serialized = new SerializedObject(target);
        serialized.FindProperty(fieldName).objectReferenceValue = value;
        serialized.ApplyModifiedProperties();
    }

    private static T GetOrAdd<T>(GameObject owner) where T : Component
    {
        T existing = owner.GetComponent<T>();
        return existing != null ? existing : owner.AddComponent<T>();
    }

    private static GameObject FindOrCreateObject(string objectName, Transform parent)
    {
        GameObject existing = GameObject.Find(objectName);
        if (existing != null)
            return existing;
        GameObject created = new GameObject(objectName);
        Undo.RegisterCreatedObjectUndo(created, "Create " + objectName);
        if (parent != null)
            created.transform.SetParent(parent, false);
        return created;
    }

    private static T FindOrCreate<T>(string objectName, Transform parent) where T : Component
    {
        return GetOrAdd<T>(FindOrCreateObject(objectName, parent));
    }

    private static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder(ProfileFolder))
            AssetDatabase.CreateFolder("Assets/Terrain", "Profiles");
    }

    private static T LoadOrCreate<T>(string path, System.Func<T> factory) where T : ScriptableObject
    {
        T existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null)
            return existing;
        T created = factory();
        AssetDatabase.CreateAsset(created, path);
        VolumeProfile volumeProfile = created as VolumeProfile;
        if (volumeProfile != null)
        {
            foreach (VolumeComponent component in volumeProfile.components)
                AssetDatabase.AddObjectToAsset(component, created);
        }
        AssetDatabase.SaveAssets();
        return created;
    }
}
#endif
