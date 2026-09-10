using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ITISKIRUHERE
{
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class AdvancedOutline : MonoBehaviour
    {
        public enum Mode
        {
            OutlineAll,
            OutlineVisible,
            OutlineHidden,
            OutlineAndSilhouette,
            SilhouetteOnly
        }

        [Serializable]
        class ListVector3
        {
            public List<Vector3> data;
        }

        [Header("References")]
        [SerializeField] Shader _outlineMaskShader;
        [SerializeField] Shader _outlineFillShader;

        [Header("General Settings")]
        [SerializeField] Mode _outlineMode = Mode.OutlineVisible;
        [SerializeField] Color _outlineColor = Color.cyan;
        [SerializeField] [Range( 0f, 10f )] float _outlineWidth = 4f;

        [Header("Aesthetic Effects")]
        [SerializeField] bool _pulseWidth;
        [SerializeField] [Range( 0.1f, 10f )] float _pulseSpeed = 2f;
        [SerializeField] [Range( 0f, 10f )] float _pulseMinWidth = 1.5f;
        [SerializeField] [Range( 0f, 10f )] float _pulseMaxWidth = 5f;

        [Space]
        [SerializeField] bool _rainbowColor;
        [SerializeField] [Range( 0.1f, 5f )] float _rainbowSpeed = 0.5f;

        [Space]
        [SerializeField] bool _dashedPattern;
        [SerializeField] [Range( 0.001f, 0.1f )] float _dashScale = 0.03f;
        [SerializeField] [Range( -50f, 50f )] float _dashSpeed = 15f;

        [Header("Performance Settings")]
        [SerializeField] bool _precomputeOutline;

        [SerializeField] [HideInInspector] List<Mesh> _bakeKeys = new List<Mesh>();
        [SerializeField] [HideInInspector] List<ListVector3> _bakeValues = new List<ListVector3>();

        // Which mesh each renderer had before the outline swapped in its own copy. Serialised
        // because a domain reload wipes the plain dictionaries below: without it the component
        // comes back, finds its own copy sitting in sharedMesh, takes that for the original and
        // clones it again. That is where the "(Clone)(Clone)(Clone)..." chains came from.
        [SerializeField] [HideInInspector] List<MeshFilter> _replacedMeshFilters = new List<MeshFilter>();
        [SerializeField] [HideInInspector] List<Mesh> _replacedMeshFilterMeshes = new List<Mesh>();
        [SerializeField] [HideInInspector] List<SkinnedMeshRenderer> _replacedSkinnedRenderers = new List<SkinnedMeshRenderer>();
        [SerializeField] [HideInInspector] List<Mesh> _replacedSkinnedMeshes = new List<Mesh>();

        Renderer[] _renderers;
        MeshFilter[] _meshFilters;
        SkinnedMeshRenderer[] _skinnedMeshRenderers;
        Material _outlineMaskMaterial;
        Material _outlineFillMaterial;
        MaterialPropertyBlock _propertyBlock;

        bool _needsUpdate;
        
        int _outlineColorId;
        int _outlineWidthId;
        int _zTestId;
        int _dashScaleId;
        int _dashSpeedId;

        List<Material> _tempMaterials = new List<Material>();
        Dictionary<Mesh, List<Vector3>> _runtimeBakeCache = new Dictionary<Mesh, List<Vector3>>();
        Dictionary<MeshFilter, Mesh> _clonedMeshes = new Dictionary<MeshFilter, Mesh>();
        Dictionary<MeshFilter, Mesh> _originalMeshes = new Dictionary<MeshFilter, Mesh>();
        Dictionary<SkinnedMeshRenderer, Mesh> _clonedSkinnedMeshes = new Dictionary<SkinnedMeshRenderer, Mesh>();
        Dictionary<SkinnedMeshRenderer, Mesh> _originalSkinnedMeshes = new Dictionary<SkinnedMeshRenderer, Mesh>();

        public Mode OutlineMode
        {
            get => _outlineMode;
            set
            {
                _outlineMode = value;
                _needsUpdate = true;
            }
        }

        public Color OutlineColor
        {
            get => _outlineColor;
            set
            {
                _outlineColor = value;
                _needsUpdate = true;
            }
        }

        public float OutlineWidth
        {
            get => _outlineWidth;
            set
            {
                _outlineWidth = value;
                _needsUpdate = true;
            }
        }

        public bool PulseWidth { get => _pulseWidth; set => _pulseWidth = value; }
        public bool RainbowColor { get => _rainbowColor; set => _rainbowColor = value; }
        public bool DashedPattern { get => _dashedPattern; set => _dashedPattern = value; }

        public int RendererCount => _renderers != null ? _renderers.Length : 0;
        public int MeshCount => ( _meshFilters != null ? _meshFilters.Length : 0 ) + ( _skinnedMeshRenderers != null ? _skinnedMeshRenderers.Length : 0 );

        public Shader OutlineMaskShader { get => _outlineMaskShader; set { _outlineMaskShader = value; _needsUpdate = true; } }
        public Shader OutlineFillShader { get => _outlineFillShader; set { _outlineFillShader = value; _needsUpdate = true; } }

        void Awake()
        {
            _propertyBlock = new MaterialPropertyBlock();
            CachePropertyIDs();
            InitializeBakeCache();
            RestoreReplacedMeshLookup();
            InitializeMaterials();
            RefreshRenderers();
            #if UNITY_EDITOR
            SubscribeToSceneSaving();
            #endif
        }

        void OnEnable()
        {
            if ( _propertyBlock == null )
            {
                _propertyBlock = new MaterialPropertyBlock();
            }
            CachePropertyIDs();
            InitializeBakeCache();
            RestoreReplacedMeshLookup();
            InitializeMaterials();
            RefreshRenderers();
            #if UNITY_EDITOR
            SubscribeToSceneSaving();
            #endif
        }

        void OnDisable()
        {
            #if UNITY_EDITOR
            UnsubscribeFromSceneSaving();
            #endif
            CleanAllOutlineMaterialsFromRenderers();
            ClearClonedMeshes();
        }

        #if UNITY_EDITOR
        // The copies are HideAndDontSave, so whatever sits in sharedMesh when the scene is
        // written decides what the file gets: leave a copy there and the mesh slot saves as
        // empty. So the originals go back before the write and the copies return after it.
        void SubscribeToSceneSaving()
        {
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving -= HandleSceneSaving;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving += HandleSceneSaving;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaved -= HandleSceneSaved;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaved += HandleSceneSaved;
        }

        void UnsubscribeFromSceneSaving()
        {
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving -= HandleSceneSaving;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaved -= HandleSceneSaved;
        }

        void HandleSceneSaving( UnityEngine.SceneManagement.Scene savingScene, string path )
        {
            if ( this == null || savingScene != gameObject.scene ) return;
            ClearClonedMeshes();
        }

        void HandleSceneSaved( UnityEngine.SceneManagement.Scene savedScene )
        {
            if ( this == null || savedScene != gameObject.scene ) return;
            LoadSmoothNormals();
        }
        #endif

        void OnDestroy()
        {
            if ( _outlineMaskMaterial )
            {
                DestroyOutlineObject( _outlineMaskMaterial );
            }
            if ( _outlineFillMaterial )
            {
                DestroyOutlineObject( _outlineFillMaterial );
            }
            ClearClonedMeshes();
        }

        void OnTransformChildrenChanged()
        {
            RefreshRenderers();
        }

        void Update()
        {
            if ( _needsUpdate )
            {
                _needsUpdate = false;
                UpdateStaticMaterialProperties();
            }

            HandleDynamicEffects();
        }

        void OnValidate()
        {
            _needsUpdate = true;
            InitializeMaterials();

            if ( _pulseMinWidth > _pulseMaxWidth )
            {
                _pulseMinWidth = _pulseMaxWidth;
            }
            if ( _pulseMaxWidth < _pulseMinWidth )
            {
                _pulseMaxWidth = _pulseMinWidth;
            }
            if ( _dashScale < 0f )
            {
                _dashScale = 0f;
            }

            #if UNITY_EDITOR
            if ( !_precomputeOutline && _bakeKeys.Count != 0 || _bakeKeys.Count != _bakeValues.Count )
            {
                _bakeKeys.Clear();
                _bakeValues.Clear();
            }

            if ( _precomputeOutline && _bakeKeys.Count == 0 )
            {
                Bake();
            }
            #endif
        }

        public void RefreshRenderers()
        {
            CacheComponents();
            CleanAllOutlineMaterialsFromRenderers();
            LoadSmoothNormals();
            ApplyMaterialsToRenderers();
            _needsUpdate = true;
        }

        void CacheComponents()
        {
            _renderers = GetComponentsInChildren<Renderer>();
            _meshFilters = GetComponentsInChildren<MeshFilter>();
            _skinnedMeshRenderers = GetComponentsInChildren<SkinnedMeshRenderer>();
        }

        void CachePropertyIDs()
        {
            _outlineColorId = Shader.PropertyToID( "_OutlineColor" );
            _outlineWidthId = Shader.PropertyToID( "_OutlineWidth" );
            _zTestId = Shader.PropertyToID( "_ZTest" );
            _dashScaleId = Shader.PropertyToID( "_DashScale" );
            _dashSpeedId = Shader.PropertyToID( "_DashSpeed" );
        }

        void InitializeBakeCache()
        {
            _runtimeBakeCache.Clear();
            if ( _bakeKeys != null && _bakeValues != null )
            {
                int count = Mathf.Min( _bakeKeys.Count, _bakeValues.Count );
                for ( int i = 0; i < count; i++ )
                {
                    if ( _bakeKeys[i] != null && _bakeValues[i] != null )
                    {
                        _runtimeBakeCache[_bakeKeys[i]] = _bakeValues[i].data;
                    }
                }
            }
        }

        void InitializeMaterials()
        {
            if ( _outlineMaskMaterial && _outlineFillMaterial ) return;

            if ( !_outlineMaskShader )
            {
                _outlineMaskShader = Shader.Find( "Custom/Advanced Outline Mask" );
            }
            if ( !_outlineFillShader )
            {
                _outlineFillShader = Shader.Find( "Custom/Advanced Outline Fill" );
            }

            if ( !_outlineMaskMaterial && _outlineMaskShader )
            {
                _outlineMaskMaterial = new Material( _outlineMaskShader );
                _outlineMaskMaterial.name = "AdvancedOutlineMask (Instance)";
            }

            if ( !_outlineFillMaterial && _outlineFillShader )
            {
                _outlineFillMaterial = new Material( _outlineFillShader );
                _outlineFillMaterial.name = "AdvancedOutlineFill (Instance)";
            }
        }

        void ApplyMaterialsToRenderers()
        {
            if ( _renderers == null ) return;

            foreach ( Renderer renderer in _renderers )
            {
                if ( !renderer ) continue;

                _tempMaterials.Clear();
                renderer.GetSharedMaterials( _tempMaterials );

                _tempMaterials.RemoveAll( mat => mat != null && 
                    ( mat.shader == _outlineMaskShader || mat.shader == _outlineFillShader ) );

                if ( _outlineMaskMaterial )
                {
                    _tempMaterials.Add( _outlineMaskMaterial );
                }
                if ( _outlineFillMaterial )
                {
                    _tempMaterials.Add( _outlineFillMaterial );
                }

                renderer.sharedMaterials = _tempMaterials.ToArray();
            }
            _tempMaterials.Clear();
        }

        void CleanAllOutlineMaterialsFromRenderers()
        {
            if ( _renderers == null ) return;

            foreach ( Renderer renderer in _renderers )
            {
                if ( !renderer ) continue;

                _tempMaterials.Clear();
                renderer.GetSharedMaterials( _tempMaterials );

                _tempMaterials.RemoveAll( mat => mat != null && 
                    ( mat.shader == _outlineMaskShader || mat.shader == _outlineFillShader ) );

                renderer.sharedMaterials = _tempMaterials.ToArray();
            }
            _tempMaterials.Clear();
        }

        void LoadSmoothNormals()
        {
            if ( _meshFilters == null ) return;

            foreach ( MeshFilter meshFilter in _meshFilters )
            {
                if ( !meshFilter ) continue;
                
                Mesh originalMesh;
                if ( !_originalMeshes.TryGetValue( meshFilter, out originalMesh ) )
                {
                    originalMesh = meshFilter.sharedMesh;
                }
                if ( !originalMesh ) continue;

                // Geometry that lives in the scene rather than in an asset - ProBuilder's, for
                // one - must not be copied: the copy is not saved, so the slot would write out
                // empty and the object would lose its mesh. Writing the extra UV channel
                // straight into it is harmless, nothing else shares it.
                if ( !IsPersistentAsset( originalMesh ) )
                {
                    List<Vector3> sceneNormals;
                    if ( !_runtimeBakeCache.TryGetValue( originalMesh, out sceneNormals ) )
                    {
                        sceneNormals = SmoothNormals( originalMesh );
                        _runtimeBakeCache[originalMesh] = sceneNormals;
                    }

                    originalMesh.SetUVs( 3, sceneNormals );

                    Renderer sceneRenderer = meshFilter.GetComponent<Renderer>();
                    if ( sceneRenderer )
                    {
                        CombineSubmeshes( originalMesh, sceneRenderer.sharedMaterials );
                    }
                    continue;
                }

                Mesh meshCopy;
                if ( !_clonedMeshes.TryGetValue( meshFilter, out meshCopy ) )
                {
                    meshCopy = Instantiate( originalMesh );
                    // Instantiate appends "(Clone)" and hands back an object the scene would
                    // happily serialise. Neither is wanted: the copy is derived data, rebuilt
                    // on load, and saving it embeds a full mesh in the scene file.
                    meshCopy.name = originalMesh.name;
                    meshCopy.hideFlags = HideFlags.HideAndDontSave;
                    _clonedMeshes[meshFilter] = meshCopy;
                    _originalMeshes[meshFilter] = originalMesh;
                    RecordReplacedMesh( meshFilter, originalMesh );
                    meshFilter.sharedMesh = meshCopy;
                }

                List<Vector3> smoothNormals;
                if ( !_runtimeBakeCache.TryGetValue( originalMesh, out smoothNormals ) )
                {
                    smoothNormals = SmoothNormals( originalMesh );
                    _runtimeBakeCache[originalMesh] = smoothNormals;
                }

                meshCopy.SetUVs( 3, smoothNormals );

                Renderer renderer = meshFilter.GetComponent<Renderer>();
                if ( renderer )
                {
                    CombineSubmeshes( meshCopy, renderer.sharedMaterials );
                }
            }

            if ( _skinnedMeshRenderers != null )
            {
                foreach ( SkinnedMeshRenderer skinnedMeshRenderer in _skinnedMeshRenderers )
                {
                    if ( !skinnedMeshRenderer ) continue;
                    
                    Mesh originalMesh;
                    if ( !_originalSkinnedMeshes.TryGetValue( skinnedMeshRenderer, out originalMesh ) )
                    {
                        originalMesh = skinnedMeshRenderer.sharedMesh;
                    }
                    if ( !originalMesh ) continue;

                    Mesh meshCopy;
                    if ( !_clonedSkinnedMeshes.TryGetValue( skinnedMeshRenderer, out meshCopy ) )
                    {
                        meshCopy = Instantiate( originalMesh );
                        meshCopy.name = originalMesh.name;
                        meshCopy.hideFlags = HideFlags.HideAndDontSave;
                        _clonedSkinnedMeshes[skinnedMeshRenderer] = meshCopy;
                        _originalSkinnedMeshes[skinnedMeshRenderer] = originalMesh;
                        RecordReplacedSkinnedMesh( skinnedMeshRenderer, originalMesh );
                        skinnedMeshRenderer.sharedMesh = meshCopy;
                    }

                    meshCopy.uv4 = new Vector2[meshCopy.vertexCount];
                    CombineSubmeshes( meshCopy, skinnedMeshRenderer.sharedMaterials );
                }
            }
        }

        List<Vector3> SmoothNormals( Mesh mesh )
        {
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            List<Vector3> smoothNormals = new List<Vector3>( normals );

            Dictionary<Vector3, List<int>> groups = new Dictionary<Vector3, List<int>>( vertices.Length );

            for ( int i = 0; i < vertices.Length; i++ )
            {
                Vector3 vertex = vertices[i];
                List<int> group;
                if ( !groups.TryGetValue( vertex, out group ) )
                {
                    group = new List<int>();
                    groups[vertex] = group;
                }
                group.Add( i );
            }

            foreach ( KeyValuePair<Vector3, List<int>> group in groups )
            {
                if ( group.Value.Count == 1 ) continue;

                Vector3 smoothNormal = Vector3.zero;
                for ( int i = 0; i < group.Value.Count; i++ )
                {
                    smoothNormal += normals[group.Value[i]];
                }

                smoothNormal.Normalize();

                for ( int i = 0; i < group.Value.Count; i++ )
                {
                    smoothNormals[group.Value[i]] = smoothNormal;
                }
            }

            return smoothNormals;
        }

        void CombineSubmeshes( Mesh mesh, Material[] materials )
        {
            if ( mesh.subMeshCount == materials.Length + 1 ) return;
            if ( mesh.subMeshCount == 1 ) return;
            if ( mesh.subMeshCount > materials.Length ) return;

            mesh.subMeshCount++;
            mesh.SetTriangles( mesh.triangles, mesh.subMeshCount - 1 );
        }

        #if UNITY_EDITOR
        public void EditorBake()
        {
            Bake();
            InitializeBakeCache();
            RefreshRenderers();
        }

        void Bake()
        {
            var bakedMeshes = new HashSet<Mesh>();
            _bakeKeys.Clear();
            _bakeValues.Clear();

            CacheComponents();
            if ( _meshFilters == null ) return;

            foreach ( MeshFilter meshFilter in _meshFilters )
            {
                if ( !meshFilter ) continue;

                // Off the original, never off sharedMesh: by the time this runs the outline has
                // already swapped its own copy in there, and keying the cache by that copy both
                // misses on the next load and drags a mesh nothing else references into the
                // scene file - which is how one scene grew to 863 saved meshes.
                Mesh sourceMesh = ResolveOriginalMesh( meshFilter );
                if ( !sourceMesh ) continue;
                if ( !bakedMeshes.Add( sourceMesh ) ) continue;

                List<Vector3> smoothNormals = SmoothNormals( sourceMesh );
                _bakeKeys.Add( sourceMesh );
                _bakeValues.Add( new ListVector3() { data = smoothNormals } );
            }
        }
        #endif

        static bool IsPersistentAsset( Mesh mesh )
        {
            if ( !mesh ) return false;
            #if UNITY_EDITOR
            return UnityEditor.AssetDatabase.Contains( mesh );
            #else
            // No scene file to damage at runtime, so the copy is always the safer choice.
            return true;
            #endif
        }

        Mesh ResolveOriginalMesh( MeshFilter meshFilter )
        {
            if ( !meshFilter ) return null;

            Mesh originalMesh;
            if ( _originalMeshes.TryGetValue( meshFilter, out originalMesh ) && originalMesh )
            {
                return originalMesh;
            }

            int index = _replacedMeshFilters.IndexOf( meshFilter );
            if ( index >= 0 && index < _replacedMeshFilterMeshes.Count && _replacedMeshFilterMeshes[index] )
            {
                return _replacedMeshFilterMeshes[index];
            }

            return meshFilter.sharedMesh;
        }

        void UpdateStaticMaterialProperties()
        {
            if ( !_outlineMaskMaterial || !_outlineFillMaterial ) return;

            switch ( _outlineMode )
            {
                case Mode.OutlineAll:
                    _outlineMaskMaterial.SetFloat( _zTestId, (float)UnityEngine.Rendering.CompareFunction.Always );
                    _outlineFillMaterial.SetFloat( _zTestId, (float)UnityEngine.Rendering.CompareFunction.Always );
                    break;

                case Mode.OutlineVisible:
                    _outlineMaskMaterial.SetFloat( _zTestId, (float)UnityEngine.Rendering.CompareFunction.Always );
                    _outlineFillMaterial.SetFloat( _zTestId, (float)UnityEngine.Rendering.CompareFunction.LessEqual );
                    break;

                case Mode.OutlineHidden:
                    _outlineMaskMaterial.SetFloat( _zTestId, (float)UnityEngine.Rendering.CompareFunction.Always );
                    _outlineFillMaterial.SetFloat( _zTestId, (float)UnityEngine.Rendering.CompareFunction.Greater );
                    break;

                case Mode.OutlineAndSilhouette:
                    _outlineMaskMaterial.SetFloat( _zTestId, (float)UnityEngine.Rendering.CompareFunction.LessEqual );
                    _outlineFillMaterial.SetFloat( _zTestId, (float)UnityEngine.Rendering.CompareFunction.Always );
                    break;

                case Mode.SilhouetteOnly:
                    _outlineMaskMaterial.SetFloat( _zTestId, (float)UnityEngine.Rendering.CompareFunction.LessEqual );
                    _outlineFillMaterial.SetFloat( _zTestId, (float)UnityEngine.Rendering.CompareFunction.Greater );
                    break;
            }

            // Write static properties via property block
            if ( _propertyBlock == null )
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            _propertyBlock.Clear();
            _propertyBlock.SetColor( _outlineColorId, _outlineColor );
            _propertyBlock.SetFloat( _outlineWidthId, _outlineMode == Mode.SilhouetteOnly ? 0f : _outlineWidth );
            _propertyBlock.SetFloat( _dashScaleId, _dashedPattern ? _dashScale : 0f );
            _propertyBlock.SetFloat( _dashSpeedId, _dashedPattern ? _dashSpeed : 0f );

            ApplyPropertyBlock();
        }

        void HandleDynamicEffects()
        {
            if ( !_outlineFillMaterial ) return;

            bool hasAnimations = _pulseWidth || _rainbowColor || _dashedPattern;
            if ( !hasAnimations ) return;

            Color color = _outlineColor;
            if ( _rainbowColor )
            {
                float hue = ( Time.time * _rainbowSpeed ) % 1.0f;
                color = Color.HSVToRGB( hue, 1f, 1f );
            }

            float width = _outlineWidth;
            if ( _outlineMode == Mode.SilhouetteOnly )
            {
                width = 0f;
            }
            else if ( _pulseWidth )
            {
                float lerpVal = ( Mathf.Sin( Time.time * _pulseSpeed ) + 1f ) / 2f;
                width = Mathf.Lerp( _pulseMinWidth, _pulseMaxWidth, lerpVal );
            }

            float scale = _dashedPattern ? _dashScale : 0f;
            float speed = _dashedPattern ? _dashSpeed : 0f;

            if ( _propertyBlock == null )
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            _propertyBlock.Clear();
            _propertyBlock.SetColor( _outlineColorId, color );
            _propertyBlock.SetFloat( _outlineWidthId, width );
            _propertyBlock.SetFloat( _dashScaleId, scale );
            _propertyBlock.SetFloat( _dashSpeedId, speed );

            ApplyPropertyBlock();
        }

        void ApplyPropertyBlock()
        {
            if ( _renderers == null ) return;

            foreach ( Renderer renderer in _renderers )
            {
                if ( renderer )
                {
                    renderer.SetPropertyBlock( _propertyBlock );
                }
            }
        }

        // Rebuilds the original-mesh dictionaries from the serialised lists after a domain
        // reload, and puts the originals back on any renderer whose copy did not survive it.
        void RestoreReplacedMeshLookup()
        {
            for ( int i = 0; i < _replacedMeshFilters.Count && i < _replacedMeshFilterMeshes.Count; i++ )
            {
                MeshFilter meshFilter = _replacedMeshFilters[i];
                Mesh originalMesh = _replacedMeshFilterMeshes[i];

                if ( !meshFilter || !originalMesh ) continue;

                _originalMeshes[meshFilter] = originalMesh;

                // The copy is HideAndDontSave, so a reload leaves the slot empty rather than
                // holding a stale mesh; either way the original is the right thing to start from.
                if ( !meshFilter.sharedMesh || !_clonedMeshes.ContainsKey( meshFilter ) )
                {
                    meshFilter.sharedMesh = originalMesh;
                }
            }

            for ( int i = 0; i < _replacedSkinnedRenderers.Count && i < _replacedSkinnedMeshes.Count; i++ )
            {
                SkinnedMeshRenderer skinnedMeshRenderer = _replacedSkinnedRenderers[i];
                Mesh originalMesh = _replacedSkinnedMeshes[i];

                if ( !skinnedMeshRenderer || !originalMesh ) continue;

                _originalSkinnedMeshes[skinnedMeshRenderer] = originalMesh;

                if ( !skinnedMeshRenderer.sharedMesh || !_clonedSkinnedMeshes.ContainsKey( skinnedMeshRenderer ) )
                {
                    skinnedMeshRenderer.sharedMesh = originalMesh;
                }
            }
        }

        void RecordReplacedMesh( MeshFilter meshFilter, Mesh originalMesh )
        {
            int index = _replacedMeshFilters.IndexOf( meshFilter );
            if ( index >= 0 && index < _replacedMeshFilterMeshes.Count )
            {
                _replacedMeshFilterMeshes[index] = originalMesh;
                return;
            }

            _replacedMeshFilters.Add( meshFilter );
            _replacedMeshFilterMeshes.Add( originalMesh );
        }

        void RecordReplacedSkinnedMesh( SkinnedMeshRenderer skinnedMeshRenderer, Mesh originalMesh )
        {
            int index = _replacedSkinnedRenderers.IndexOf( skinnedMeshRenderer );
            if ( index >= 0 && index < _replacedSkinnedMeshes.Count )
            {
                _replacedSkinnedMeshes[index] = originalMesh;
                return;
            }

            _replacedSkinnedRenderers.Add( skinnedMeshRenderer );
            _replacedSkinnedMeshes.Add( originalMesh );
        }

        void ClearClonedMeshes()
        {
            _replacedMeshFilters.Clear();
            _replacedMeshFilterMeshes.Clear();
            _replacedSkinnedRenderers.Clear();
            _replacedSkinnedMeshes.Clear();

            foreach ( KeyValuePair<MeshFilter, Mesh> pair in _originalMeshes )
            {
                if ( pair.Key && pair.Value )
                {
                    pair.Key.sharedMesh = pair.Value;
                }
            }
            _originalMeshes.Clear();

            foreach ( KeyValuePair<MeshFilter, Mesh> pair in _clonedMeshes )
            {
                if ( pair.Value )
                {
                    DestroyOutlineObject( pair.Value );
                }
            }
            _clonedMeshes.Clear();

            foreach ( KeyValuePair<SkinnedMeshRenderer, Mesh> pair in _originalSkinnedMeshes )
            {
                if ( pair.Key && pair.Value )
                {
                    pair.Key.sharedMesh = pair.Value;
                }
            }
            _originalSkinnedMeshes.Clear();

            foreach ( KeyValuePair<SkinnedMeshRenderer, Mesh> pair in _clonedSkinnedMeshes )
            {
                if ( pair.Value )
                {
                    DestroyOutlineObject( pair.Value );
                }
            }
            _clonedSkinnedMeshes.Clear();
        }

        void DestroyOutlineObject( UnityEngine.Object obj )
        {
            if ( !obj ) return;
            if ( Application.isPlaying )
            {
                Destroy( obj );
            }
            else
            {
                DestroyImmediate( obj );
            }
        }
    }
}
