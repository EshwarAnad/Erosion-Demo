﻿using System.Collections.Generic;
using UnityEngine;

public class TerrainGenerator : MonoBehaviour {

    public bool printTimers;

    [Header ("Mesh Settings")]
    public int mapSize = 255;
    public float scale = 20;
    public float elevationScale = 10;
    public Material material;

    [Header ("Erosion Settings")]
    public ComputeShader erosion;
    public int numErosionIterations = 50000;
    public int erosionBrushRadius = 3;

    public int maxLifetime = 30;
    public float sedimentCapacityFactor = 3;
    public float minSedimentCapacity = .01f;
    public float depositSpeed = 0.3f;
    public float erodeSpeed = 0.3f;

    public float evaporateSpeed = .01f;
    public float gravity = 4;
    public float startSpeed = 1;
    public float startWater = 1;
    [Range (0, 1)]
    public float inertia = 0.3f;

    // Internal
    bool heightmapUpdatedSinceErosion = true;
    float[] map;
    Mesh mesh;
    int mapSizeWithBorder;

    MeshRenderer meshRenderer;
    MeshFilter meshFilter;
    HeightMapGenerator heightMapGenerator;
    int[] randomIndices;
    int mapSizeOld;
    ComputeBuffer mapBuffer;
    bool requestUpdate;

    void OnValidate () {
        if (Application.isPlaying) {
            heightMapGenerator = FindObjectOfType<HeightMapGenerator> ();
            SettingsChanged ();
        }
    }

    void Awake () {
        heightMapGenerator = FindObjectOfType<HeightMapGenerator> ();
        GenerateHeightMap ();
        Erode ();
        ConstructMesh ();
    }

    public void SetGrass (float v) {
        material.SetFloat("_GrassSlopeThreshold", v);
    }

    public void SetOctaves (float v) {
        heightMapGenerator.numOctaves = (int) v;
        SettingsChanged ();
    }

    public void SetPersistence (float v) {
        heightMapGenerator.persistence = v;
        SettingsChanged ();
    }

    public void SetLacunarity (float v) {
        heightMapGenerator.lacunarity = v;
        SettingsChanged ();
    }

    public void SetOffset (float v) {
        heightMapGenerator.offset = v;
        SettingsChanged ();
    }

    public void SetScale (float v) {
        heightMapGenerator.initialScale = v;
        SettingsChanged ();
    }

    public void SetHeight (float v) {
        elevationScale = v;
        //SimpleGenerate();
        ConstructMesh ();
    }

    public void RandomSeed () {
        heightMapGenerator.seed = Random.Range (-1000, 1000);
        SettingsChanged ();
    }

    public void SimpleGenerate () {
        GenerateHeightMap ();
        ConstructMesh ();
    }

    public void SetErosionIterations (float i) {
        numErosionIterations = (int) i;
        SettingsChanged ();
    }

    public void SetErosionSedimentCapacity (float i) {
        sedimentCapacityFactor = i;
        SettingsChanged ();
    }

    public void SetErosionEvaporation (float i) {
        evaporateSpeed = i;
        SettingsChanged ();
    }

    public void SetErosionInertia (float i) {
        inertia = i;
        SettingsChanged ();
    }

    public void SetErosionRadius (float i) {
        erosionBrushRadius = (int) i;
        SettingsChanged ();
    }

    public void SettingsChanged () {
        requestUpdate = true;
    }

    void Update () {
        if (requestUpdate) {
            requestUpdate = false;
            GenerateHeightMap ();
            Erode ();
            ConstructMesh ();
        }
    }

    public void GenerateHeightMap () {
        if (heightMapGenerator == null) {
            heightMapGenerator = GetComponent<HeightMapGenerator> ();
        }
        mapSizeWithBorder = mapSize + erosionBrushRadius * 2;
        map = heightMapGenerator.GenerateHeightMap (mapSizeWithBorder);
        heightmapUpdatedSinceErosion = true;
    }

    public void Erode () {
        if (numErosionIterations <= 0) {
            return;
        }
        int numThreads = Mathf.Max (1, numErosionIterations / 1024);

        // Create brush
        List<int> brushIndexOffsets = new List<int> ();
        List<float> brushWeights = new List<float> ();

        float weightSum = 0;
        for (int brushY = -erosionBrushRadius; brushY <= erosionBrushRadius; brushY++) {
            for (int brushX = -erosionBrushRadius; brushX <= erosionBrushRadius; brushX++) {
                float sqrDst = brushX * brushX + brushY * brushY;
                if (sqrDst < erosionBrushRadius * erosionBrushRadius) {
                    brushIndexOffsets.Add (brushY * mapSize + brushX);
                    float brushWeight = 1 - Mathf.Sqrt (sqrDst) / erosionBrushRadius;
                    weightSum += brushWeight;
                    brushWeights.Add (brushWeight);
                }
            }
        }
        for (int i = 0; i < brushWeights.Count; i++) {
            brushWeights[i] /= weightSum;
        }

        // Send brush data to compute shader
        ComputeBuffer brushIndexBuffer = new ComputeBuffer (brushIndexOffsets.Count, sizeof (int));
        ComputeBuffer brushWeightBuffer = new ComputeBuffer (brushWeights.Count, sizeof (int));
        brushIndexBuffer.SetData (brushIndexOffsets);
        brushWeightBuffer.SetData (brushWeights);
        erosion.SetBuffer (0, "brushIndices", brushIndexBuffer);
        erosion.SetBuffer (0, "brushWeights", brushWeightBuffer);

        // Generate random indices for droplet placement
        if (randomIndices == null || randomIndices.Length != numErosionIterations || mapSizeOld != mapSize) {
            mapSizeOld = mapSize;
            randomIndices = new int[numErosionIterations];
            for (int i = 0; i < numErosionIterations; i++) {
                int randomX = Random.Range (erosionBrushRadius, mapSize + erosionBrushRadius);
                int randomY = Random.Range (erosionBrushRadius, mapSize + erosionBrushRadius);
                randomIndices[i] = randomY * mapSize + randomX;
            }
        }

        // Send random indices to compute shader
        ComputeBuffer randomIndexBuffer = new ComputeBuffer (randomIndices.Length, sizeof (int));
        randomIndexBuffer.SetData (randomIndices);
        erosion.SetBuffer (0, "randomIndices", randomIndexBuffer);

        // Heightmap buffer
        if (heightmapUpdatedSinceErosion) {
            if (mapBuffer != null) {
                mapBuffer.Release ();
            }
            mapBuffer = new ComputeBuffer (map.Length, sizeof (float));
            mapBuffer.SetData (map);
            erosion.SetBuffer (0, "map", mapBuffer);
        }

        // Settings
        erosion.SetInt ("borderSize", erosionBrushRadius);
        erosion.SetInt ("mapSize", mapSizeWithBorder);
        erosion.SetInt ("brushLength", brushIndexOffsets.Count);
        erosion.SetInt ("maxLifetime", maxLifetime);
        erosion.SetFloat ("inertia", inertia);
        erosion.SetFloat ("sedimentCapacityFactor", sedimentCapacityFactor);
        erosion.SetFloat ("minSedimentCapacity", minSedimentCapacity);
        erosion.SetFloat ("depositSpeed", depositSpeed);
        erosion.SetFloat ("erodeSpeed", erodeSpeed);
        erosion.SetFloat ("evaporateSpeed", evaporateSpeed);
        erosion.SetFloat ("gravity", gravity);
        erosion.SetFloat ("startSpeed", startSpeed);
        erosion.SetFloat ("startWater", startWater);

        // Run compute shader
        erosion.Dispatch (0, numThreads, 1, 1);

        mapBuffer.GetData (map);

        // Release buffers
        //mapBuffer.Release ();
        randomIndexBuffer.Release ();
        brushIndexBuffer.Release ();
        brushWeightBuffer.Release ();

        if (!Application.isPlaying) {
            mapBuffer.Release ();
        }
    }

    void OnDestroy () {
        if (mapBuffer != null) {
            mapBuffer.Release ();
        }
    }

    public void ConstructMesh () {
        Vector3[] verts = new Vector3[mapSize * mapSize];
        int[] triangles = new int[(mapSize - 1) * (mapSize - 1) * 6];
        int t = 0;

        for (int i = 0; i < mapSize * mapSize; i++) {
            int x = i % mapSize;
            int y = i / mapSize;
            int borderedMapIndex = (y + erosionBrushRadius) * mapSizeWithBorder + x + erosionBrushRadius;
            int meshMapIndex = y * mapSize + x;

            Vector2 percent = new Vector2 (x / (mapSize - 1f), y / (mapSize - 1f));
            Vector3 pos = new Vector3 (percent.x * 2 - 1, 0, percent.y * 2 - 1) * scale;

            float normalizedHeight = map[borderedMapIndex];
            pos += Vector3.up * normalizedHeight * elevationScale;
            verts[meshMapIndex] = pos;

            // Construct triangles
            if (x != mapSize - 1 && y != mapSize - 1) {
                t = (y * (mapSize - 1) + x) * 3 * 2;

                triangles[t + 0] = meshMapIndex + mapSize;
                triangles[t + 1] = meshMapIndex + mapSize + 1;
                triangles[t + 2] = meshMapIndex;

                triangles[t + 3] = meshMapIndex + mapSize + 1;
                triangles[t + 4] = meshMapIndex + 1;
                triangles[t + 5] = meshMapIndex;
                t += 6;
            }
        }

        AssignMeshComponents ();

        if (mesh == null) {
            mesh = new Mesh ();
            meshFilter.sharedMesh = mesh;
            meshRenderer.sharedMaterial = material;
        } else {
            mesh.Clear ();
        }
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = verts;
        mesh.triangles = triangles;
        mesh.RecalculateNormals ();

        material.SetFloat ("_MaxHeight", elevationScale);
    }

    void AssignMeshComponents () {
        // Find/creator mesh holder object in children
        string meshHolderName = "Mesh Holder";
        Transform meshHolder = transform.Find (meshHolderName);
        if (meshHolder == null) {
            meshHolder = new GameObject (meshHolderName).transform;
            meshHolder.transform.parent = transform;
            meshHolder.transform.localPosition = Vector3.zero;
            meshHolder.transform.localRotation = Quaternion.identity;
        }

        // Ensure mesh renderer and filter components are assigned
        if (!meshHolder.gameObject.GetComponent<MeshFilter> ()) {
            meshHolder.gameObject.AddComponent<MeshFilter> ();
        }
        if (!meshHolder.GetComponent<MeshRenderer> ()) {
            meshHolder.gameObject.AddComponent<MeshRenderer> ();
        }

        meshRenderer = meshHolder.GetComponent<MeshRenderer> ();
        meshFilter = meshHolder.GetComponent<MeshFilter> ();
    }
}