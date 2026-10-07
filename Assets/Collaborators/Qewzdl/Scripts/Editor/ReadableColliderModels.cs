using UnityEditor;

// A model imported with colliders is imported readable. The navmesh is built
// at run time from the colliders of the map, and in a built game it cannot
// read a mesh that was not imported readable: it leaves it out, and the
// enemy has no floor there. In the editor it reads it anyway, so nothing
// shows until the game is built.
public sealed class ReadableColliderModels : AssetPostprocessor
{
    private void OnPreprocessModel()
    {
        if (assetImporter is ModelImporter model && model.addCollider)
            model.isReadable = true;
    }
}
