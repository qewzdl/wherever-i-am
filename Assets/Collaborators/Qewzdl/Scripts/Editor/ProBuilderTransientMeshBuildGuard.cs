using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.ProBuilder;

public sealed class ProBuilderTransientMeshBuildGuard : IPreprocessBuildWithReport
{
    public int callbackOrder => -1000;

    public void OnPreprocessBuild(BuildReport report)
    {
        ProBuilderMesh[] meshes = Resources.FindObjectsOfTypeAll<ProBuilderMesh>();
        int removedCount = 0;

        for (int i = 0; i < meshes.Length; i++)
        {
            ProBuilderMesh mesh = meshes[i];

            if (!IsEmptyTransientPreview(mesh))
            {
                continue;
            }

            Object.DestroyImmediate(mesh.gameObject);
            removedCount++;
        }

        if (removedCount > 0)
        {
            Debug.Log(
                $"{nameof(ProBuilderTransientMeshBuildGuard)} removed {removedCount} " +
                "empty transient ProBuilder preview object(s) before the build."
            );
        }
    }

    private static bool IsEmptyTransientPreview(ProBuilderMesh mesh)
    {
        if (mesh == null || EditorUtility.IsPersistent(mesh))
        {
            return false;
        }

        GameObject gameObject = mesh.gameObject;

        if (gameObject == null || (gameObject.hideFlags & HideFlags.DontSave) == 0)
        {
            return false;
        }

        return mesh.vertexCount == 0 && mesh.faceCount == 0;
    }
}
