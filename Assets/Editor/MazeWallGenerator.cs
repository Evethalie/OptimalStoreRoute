

using UnityEditor;
using UnityEngine;
using static StarterAssets.Readme;

public class MazeWallGenerator : EditorWindow
{
    // Source
    MeshRenderer floor;
    Texture textureOverride;

    // Detection
    int sampleResolution = 256;
    float darkThreshold = 0.35f;
    int dilatePixels = 0;

    // Walls
    GameObject wallPrefab;
    Material wallMaterial;
    float wallHeight = 1f;
    float minThickness = 0.05f;
    bool markStatic = true;

    string lastResult = "";

    [MenuItem("Tools/Maze Wall Generator")]
    static void Open() => GetWindow<MazeWallGenerator>("Maze Walls");

    void OnGUI()
    {
        EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
        floor = (MeshRenderer)EditorGUILayout.ObjectField("Floor", floor, typeof(MeshRenderer), true);
        if (GUILayout.Button("Use Selected Object") && Selection.activeGameObject != null)
            floor = Selection.activeGameObject.GetComponent<MeshRenderer>();
        textureOverride = (Texture)EditorGUILayout.ObjectField(
            new GUIContent("Texture Override", "Leave empty to use the floor material's _BaseMap or _MainTex."),
            textureOverride, typeof(Texture), false);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Detection", EditorStyles.boldLabel);
        sampleResolution = EditorGUILayout.IntSlider(
            new GUIContent("Sample Resolution", "Width in pixels the texture is resampled to. Lower values give fewer, chunkier walls."),
            sampleResolution, 16, 2048);
        darkThreshold = EditorGUILayout.Slider(
            new GUIContent("Dark Threshold", "Pixels with brightness below this value count as wall."),
            darkThreshold, 0f, 1f);
        dilatePixels = EditorGUILayout.IntSlider(
            new GUIContent("Dilate Pixels", "Thickens detected lines. Useful when thin lines break apart."),
            dilatePixels, 0, 8);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Walls", EditorStyles.boldLabel);
        wallPrefab = (GameObject)EditorGUILayout.ObjectField(
            new GUIContent("Wall Prefab", "Optional. Must be a 1x1x1 box centered on its pivot. Leave empty to use a cube."),
            wallPrefab, typeof(GameObject), false);
        wallMaterial = (Material)EditorGUILayout.ObjectField("Wall Material", wallMaterial, typeof(Material), false);
        wallHeight = EditorGUILayout.FloatField("Wall Height", wallHeight);
        minThickness = EditorGUILayout.FloatField(
            new GUIContent("Min Thickness", "Walls thinner than this (in world units) get widened."),
            minThickness);
        markStatic = EditorGUILayout.Toggle("Mark Static", markStatic);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(floor == null))
        {
            if (GUILayout.Button("Generate Walls", GUILayout.Height(30))) Generate();
            if (GUILayout.Button("Clear Walls")) Clear(true);
        }

        if (!string.IsNullOrEmpty(lastResult))
            EditorGUILayout.HelpBox(lastResult, MessageType.Info);
    }

    string RootName => $"Maze Walls ({floor.name})";

    void Clear(bool ownUndoGroup)
    {
        var existing = GameObject.Find(RootName);
        if (existing == null) return;
        if (ownUndoGroup) Undo.SetCurrentGroupName("Clear Maze Walls");
        Undo.DestroyObjectImmediate(existing);
    }

    void Generate()
    {
        var filter = floor.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null)
        {
            lastResult = "The floor needs a MeshFilter with a mesh.";
            return;
        }

        // Find the texture and its tiling/offset.
        var mat = floor.sharedMaterial;
        string prop = null;
        Texture tex = textureOverride;
        if (mat != null)
        {
            if (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null) prop = "_BaseMap";
            else if (mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null) prop = "_MainTex";
            if (tex == null && prop != null) tex = mat.GetTexture(prop);
        }
        if (tex == null)
        {
            lastResult = "No texture found. Assign one to the floor material or use Texture Override.";
            return;
        }
        Vector2 tiling = prop != null ? mat.GetTextureScale(prop) : Vector2.one;
        Vector2 offset = prop != null ? mat.GetTextureOffset(prop) : Vector2.zero;

        // Work out how mesh UVs map to local positions.
        if (!SolveUvToLocal(filter.sharedMesh, out Vector3 originLocal, out Vector2 originUv,
                            out Vector3 dPdU, out Vector3 dPdV, out Vector3 localNormal))
        {
            lastResult = "Couldn't map the mesh UVs to positions. Make sure the floor mesh has UVs.";
            return;
        }

        // Resample the texture into a readable grid.
        int w = sampleResolution;
        int h = Mathf.Max(1, Mathf.RoundToInt(sampleResolution * (float)tex.height / tex.width));
        Color[] pixels = ReadTexture(tex, w, h);

        bool[,] wall = new bool[w, h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color c = pixels[y * w + x];
                wall[x, y] = c.a > 0.5f && c.grayscale < darkThreshold;
            }
        for (int i = 0; i < dilatePixels; i++) wall = Dilate(wall, w, h);

        // Rebuild the walls.
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Generate Maze Walls");
        Clear(false);

        var root = new GameObject(RootName);
        Transform ft = floor.transform;

        Vector3 worldNormal = ft.TransformDirection(localNormal).normalized;
        if (worldNormal == Vector3.zero) worldNormal = ft.up;

        // Greedy merge: grow each unclaimed dark pixel into the largest rectangle possible.
        bool[,] used = new bool[w, h];
        int count = 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (!wall[x, y] || used[x, y]) continue;

                int rw = 1;
                while (x + rw < w && wall[x + rw, y] && !used[x + rw, y]) rw++;

                int rh = 1;
                while (y + rh < h && RowFree(wall, used, x, rw, y + rh)) rh++;

                for (int yy = y; yy < y + rh; yy++)
                    for (int xx = x; xx < x + rw; xx++)
                        used[xx, yy] = true;

                // Pixel rect -> texture UV -> mesh UV -> local -> world.
                float u0 = (x / (float)w - offset.x) / tiling.x;
                float u1 = ((x + rw) / (float)w - offset.x) / tiling.x;
                float v0 = (y / (float)h - offset.y) / tiling.y;
                float v1 = ((y + rh) / (float)h - offset.y) / tiling.y;

                Vector3 localCenter = originLocal
                    + dPdU * ((u0 + u1) * 0.5f - originUv.x)
                    + dPdV * ((v0 + v1) * 0.5f - originUv.y);

                Vector3 worldCenter = ft.TransformPoint(localCenter);
                Vector3 worldA = ft.TransformVector(dPdU * (u1 - u0));
                Vector3 worldB = ft.TransformVector(dPdV * (v1 - v0));

                Vector3 n = Vector3.Cross(worldA, worldB).normalized;
                if (Vector3.Dot(n, worldNormal) < 0f) n = -n;

                Vector3 forward = Vector3.ProjectOnPlane(worldB, n).normalized;
                float sizeA = Mathf.Max(worldA.magnitude, minThickness);
                float sizeB = Mathf.Max(worldB.magnitude, minThickness);

                GameObject go = CreateWall();
                go.name = $"Wall {count}";
                go.transform.SetParent(root.transform, false);
                go.transform.SetPositionAndRotation(
                    worldCenter + n * (wallHeight * 0.5f),
                    Quaternion.LookRotation(forward, n));
                go.transform.localScale = new Vector3(sizeA, wallHeight, sizeB);
                go.isStatic = markStatic;
                count++;
            }
        }

        root.isStatic = markStatic;
        Undo.RegisterCreatedObjectUndo(root, "Generate Maze Walls");
        Undo.CollapseUndoOperations(group);
        Selection.activeGameObject = root;

        lastResult = $"Created {count} walls from a {w}x{h} sample of '{tex.name}'.";
    }

    GameObject CreateWall()
    {
        GameObject go = wallPrefab != null
            ? (GameObject)PrefabUtility.InstantiatePrefab(wallPrefab)
            : GameObject.CreatePrimitive(PrimitiveType.Cube);

        if (wallMaterial != null)
        {
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = wallMaterial;
        }
        return go;
    }

    static bool RowFree(bool[,] wall, bool[,] used, int x, int width, int y)
    {
        for (int xx = x; xx < x + width; xx++)
            if (!wall[xx, y] || used[xx, y]) return false;
        return true;
    }

    static bool[,] Dilate(bool[,] src, int w, int h)
    {
        var dst = new bool[w, h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                dst[x, y] = src[x, y]
                    || (x > 0 && src[x - 1, y]) || (x < w - 1 && src[x + 1, y])
                    || (y > 0 && src[x, y - 1]) || (y < h - 1 && src[x, y + 1]);
        return dst;
    }

    // Blits through a RenderTexture so the source texture doesn't need Read/Write enabled.
    static Color[] ReadTexture(Texture tex, int w, int h)
    {
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(tex, rt);

        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var readable = new Texture2D(w, h, TextureFormat.RGBA32, false);
        readable.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        readable.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        Color[] pixels = readable.GetPixels();
        DestroyImmediate(readable);
        return pixels;
    }

    // Finds a triangle with usable UVs and solves the affine map
    // local = origin + dPdU * (u - originUv.x) + dPdV * (v - originUv.y).
    // Works for Unity's Plane, Quad, and most custom flat meshes regardless of UV orientation.
    static bool SolveUvToLocal(Mesh mesh, out Vector3 origin, out Vector2 originUv,
                               out Vector3 dPdU, out Vector3 dPdV, out Vector3 normal)
    {
        origin = dPdU = dPdV = normal = Vector3.zero;
        originUv = Vector2.zero;

        var verts = mesh.vertices;
        var uvs = mesh.uv;
        var tris = mesh.triangles;
        var normals = mesh.normals;
        if (uvs == null || uvs.Length != verts.Length) return false;

        for (int t = 0; t + 2 < tris.Length; t += 3)
        {
            int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
            Vector3 e1 = verts[i1] - verts[i0];
            Vector3 e2 = verts[i2] - verts[i0];
            Vector2 d1 = uvs[i1] - uvs[i0];
            Vector2 d2 = uvs[i2] - uvs[i0];

            float det = d1.x * d2.y - d2.x * d1.y;
            if (Mathf.Abs(det) < 1e-8f) continue;

            dPdU = (e1 * d2.y - e2 * d1.y) / det;
            dPdV = (e2 * d1.x - e1 * d2.x) / det;
            origin = verts[i0];
            originUv = uvs[i0];
            normal = (normals != null && normals.Length == verts.Length)
                ? normals[i0]
                : Vector3.Cross(e1, e2).normalized;
            return true;
        }
        return false;
    }
}