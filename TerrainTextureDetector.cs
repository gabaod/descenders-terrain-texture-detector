using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class TerrainTextureDetector : EditorWindow
{
    private Terrain terrain;
    private int selectedTextureIndex = 0;
    private string[] textureNames;
    private List<Vector3> boundaryPoints = new List<Vector3>();
    private List<List<Vector3>> boundaryContours = new List<List<Vector3>>();
    private int edgeCount = 0;
    private int contourCount = 0;
    private bool showBoundaries = true;
    private bool showBoundaryLines = true;
    private Color boundaryColor = Color.red;
    private float boundaryConnectionDistance = 2.0f;
    private float expansionDistance = 1.0f;
    private bool previewExpansion = false;
    private int replacementTextureIndex = 0;
    private float paintOpacity = 100f;
    private bool replaceOriginalTexture = true;
    private bool replaceWithinLoops = false;
    private GameObject centerObject;
    private float boxSizeX = 10.0f;
    private float boxSizeZ = 10.0f;
    private bool useBoxConstraint = false;
    private float[,,] backupAlphamaps = null;
    private bool hasBackup = false;

    [MenuItem("Tools/Terrain Texture Detector")]
    public static void ShowWindow()
    {
        GetWindow<TerrainTextureDetector>("Texture Detector");
    }

    void OnEnable()
    {
        SceneView.onSceneGUIDelegate += OnSceneGUI;
    }

    void OnDisable()
    {
        SceneView.onSceneGUIDelegate -= OnSceneGUI;
    }

    void OnGUI()
    {
        GUILayout.Label("Terrain Texture Boundary Detector", EditorStyles.boldLabel);

        EditorGUILayout.Space();

        terrain = (Terrain)EditorGUILayout.ObjectField("Terrain", terrain, typeof(Terrain), true);

        if (terrain == null)
        {
            EditorGUILayout.HelpBox("Please assign a Terrain object.", MessageType.Warning);
            return;
        }

        TerrainData terrainData = terrain.terrainData;

        if (terrainData.alphamapLayers == 0)
        {
            EditorGUILayout.HelpBox("Terrain has no textures painted.", MessageType.Warning);
            return;
        }

        // Build texture names for dropdown
        if (textureNames == null || textureNames.Length != terrainData.alphamapLayers)
        {
            textureNames = new string[terrainData.alphamapLayers];

#if UNITY_2018_3_OR_NEWER
            TerrainLayer[] terrainLayers = terrainData.terrainLayers;
            
            for (int i = 0; i < terrainData.alphamapLayers; i++)
            {
                if (terrainLayers != null && i < terrainLayers.Length && terrainLayers[i] != null)
                {
                    // Use the terrain layer name or diffuse texture name
                    string layerName = terrainLayers[i].name;
                    if (string.IsNullOrEmpty(layerName) && terrainLayers[i].diffuseTexture != null)
                    {
                        layerName = terrainLayers[i].diffuseTexture.name;
                    }
                    textureNames[i] = string.IsNullOrEmpty(layerName) ? "Texture " + i : layerName;
                }
                else
                {
                    textureNames[i] = "Texture " + i;
                }
            }
#else
            SplatPrototype[] splatPrototypes = terrainData.splatPrototypes;

            for (int i = 0; i < terrainData.alphamapLayers; i++)
            {
                if (splatPrototypes != null && i < splatPrototypes.Length && splatPrototypes[i].texture != null)
                {
                    textureNames[i] = splatPrototypes[i].texture.name;
                }
                else
                {
                    textureNames[i] = "Texture " + i;
                }
            }
#endif
        }

        EditorGUILayout.Space();
        selectedTextureIndex = EditorGUILayout.Popup("Select Texture", selectedTextureIndex, textureNames);

        EditorGUILayout.Space();
        boundaryColor = EditorGUILayout.ColorField("Boundary Color", boundaryColor);
        showBoundaries = EditorGUILayout.Toggle("Show Boundary Points", showBoundaries);
        showBoundaryLines = EditorGUILayout.Toggle("Show Boundary Lines", showBoundaryLines);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Boundary Connection Distance:", EditorStyles.boldLabel);
        boundaryConnectionDistance = EditorGUILayout.Slider(boundaryConnectionDistance, 0.5f, 10.0f);
        EditorGUILayout.HelpBox("Distance in world units for connecting boundary edges.", MessageType.Info);


        EditorGUILayout.Space();

        if (GUILayout.Button("Detect Texture Boundaries", GUILayout.Height(30)))
        {
            DetectBoundaries();
        }

        if (GUILayout.Button("Clear Boundaries"))
        {
            boundaryPoints.Clear();
            boundaryContours.Clear();
            edgeCount = 0;
            contourCount = 0;
            SceneView.RepaintAll();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Texture Expansion:", EditorStyles.boldLabel);
        expansionDistance = EditorGUILayout.Slider("Expansion Distance (meters)", expansionDistance, 0.5f, 20.0f);

        if (terrainData.alphamapLayers > 0)
        {
            replacementTextureIndex = EditorGUILayout.Popup("Paint With Texture", replacementTextureIndex, textureNames);
        }

        paintOpacity = EditorGUILayout.Slider("Paint Opacity (%)", paintOpacity, 1f, 100f);
        replaceOriginalTexture = EditorGUILayout.Toggle("Replace Original Texture", replaceOriginalTexture);
        replaceWithinLoops = EditorGUILayout.Toggle("Replace Within Loops Only", replaceWithinLoops);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Box Constraint:", EditorStyles.boldLabel);
        useBoxConstraint = EditorGUILayout.Toggle("Use Box Constraint", useBoxConstraint);
        
        if (useBoxConstraint)
        {
            centerObject = (GameObject)EditorGUILayout.ObjectField("Center Object", centerObject, typeof(GameObject), true);
            boxSizeX = EditorGUILayout.FloatField("Box Size X (meters)", boxSizeX);
            boxSizeZ = EditorGUILayout.FloatField("Box Size Z (meters)", boxSizeZ);
            
            if (centerObject == null)
            {
                EditorGUILayout.HelpBox("Please assign a GameObject to use as the center point.", MessageType.Warning);
            } else {
                EditorGUILayout.HelpBox("Only terrain within the blue box will be modified.", MessageType.Info);
            }
        }
        
        if (replaceWithinLoops)
        {
            EditorGUILayout.HelpBox("This will only replace texture inside the detected boundary loops, ignoring the expansion distance.", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox("Expand and repaint texture. Enable 'Replace Original Texture' to repaint inside the red edges too.", MessageType.Info);
        }

        previewExpansion = EditorGUILayout.Toggle("Preview Expansion", previewExpansion);

        EditorGUILayout.Space();

        if (GUILayout.Button("Apply Texture Expansion", GUILayout.Height(30)))
        {
            ApplyTextureExpansion();
        }

        EditorGUILayout.Space();

        if (hasBackup)
        {
            GUI.backgroundColor = Color.yellow;
            if (GUILayout.Button("Restore Backup", GUILayout.Height(25)))
            {
                RestoreBackup();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.HelpBox("A backup of the terrain textures exists. Click 'Restore Backup' to undo the last expansion.", MessageType.Info);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Results:", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Edges Found: " + edgeCount);
        EditorGUILayout.LabelField("Contours Found: " + contourCount);
        EditorGUILayout.LabelField("Boundary Points: " + boundaryPoints.Count);
        EditorGUILayout.LabelField("Boundary Loops: " + boundaryContours.Count);
    }

    void DetectBoundaries()
    {
        boundaryPoints.Clear();
        boundaryContours.Clear();
        edgeCount = 0;
        contourCount = 0;

        TerrainData terrainData = terrain.terrainData;
        int alphaWidth = terrainData.alphamapWidth;
        int alphaHeight = terrainData.alphamapHeight;

        float[,,] alphaMaps = terrainData.GetAlphamaps(0, 0, alphaWidth, alphaHeight);

        Debug.Log("Starting texture boundary detection for Texture " + selectedTextureIndex);
        Debug.Log("Alphamap resolution: " + alphaWidth + "x" + alphaHeight);

        // Create a binary map of the selected texture
        bool[,] textureMap = new bool[alphaWidth, alphaHeight];

        for (int y = 0; y < alphaHeight; y++)
        {
            for (int x = 0; x < alphaWidth; x++)
            {
                textureMap[x, y] = alphaMaps[y, x, selectedTextureIndex] > 0.5f;
            }
        }

        // Detect edges and store their pixel coordinates
        List<Vector2Int> edgePixels = new List<Vector2Int>();
        bool[,] isEdge = new bool[alphaWidth, alphaHeight];

        for (int y = 0; y < alphaHeight; y++)
        {
            for (int x = 0; x < alphaWidth; x++)
            {
                if (textureMap[x, y])
                {
                    bool isBoundary = false;

                    if (x == 0 || x == alphaWidth - 1 || y == 0 || y == alphaHeight - 1)
                    {
                        isBoundary = true;
                    }
                    else
                    {
                        if (!textureMap[x - 1, y] || !textureMap[x + 1, y] ||
                            !textureMap[x, y - 1] || !textureMap[x, y + 1])
                        {
                            isBoundary = true;
                        }
                    }

                    if (isBoundary)
                    {
                        isEdge[x, y] = true;
                        edgeCount++;
                        edgePixels.Add(new Vector2Int(x, y));

                        float worldX = (float)x / alphaWidth * terrainData.size.x;
                        float worldZ = (float)y / alphaHeight * terrainData.size.z;
                        float worldY = terrain.SampleHeight(terrain.transform.position + new Vector3(worldX, 0, worldZ));

                        Vector3 worldPos = terrain.transform.position + new Vector3(worldX, worldY, worldZ);
                        boundaryPoints.Add(worldPos);
                    }
                }
            }
        }

        // Trace boundary contours
        Debug.Log("Tracing boundary contours...");
        TraceBoundaryContours(edgePixels, isEdge, alphaWidth, alphaHeight, terrainData);

        // Count contours
        bool[,] visited = new bool[alphaWidth, alphaHeight];

        for (int y = 0; y < alphaHeight; y++)
        {
            for (int x = 0; x < alphaWidth; x++)
            {
                if (textureMap[x, y] && !visited[x, y])
                {
                    FloodFill(textureMap, visited, x, y, alphaWidth, alphaHeight);
                    contourCount++;
                }
            }
        }

        Debug.Log("Detection complete!");
        Debug.Log("Edges found: " + edgeCount);
        Debug.Log("Contours found: " + contourCount);
        Debug.Log("Boundary points: " + boundaryPoints.Count);
        Debug.Log("Boundary loops: " + boundaryContours.Count);

        SceneView.RepaintAll();
    }

    void TraceBoundaryContours(List<Vector2Int> edgePixels, bool[,] isEdge, int width, int height, TerrainData terrainData)
    {
        bool[,] visited = new bool[width, height];

        foreach (Vector2Int startPixel in edgePixels)
        {
            if (visited[startPixel.x, startPixel.y])
                continue;

            List<Vector3> contour = new List<Vector3>();
            List<Vector2Int> pixelChain = new List<Vector2Int>();

            // Start tracing from this pixel
            Vector2Int current = startPixel;

            while (true)
            {
                if (visited[current.x, current.y])
                    break;

                visited[current.x, current.y] = true;
                pixelChain.Add(current);

                // Convert to world space
                float worldX = (float)current.x / width * terrainData.size.x;
                float worldZ = (float)current.y / height * terrainData.size.z;
                float worldY = terrain.SampleHeight(terrain.transform.position + new Vector3(worldX, 0, worldZ));
                Vector3 worldPos = terrain.transform.position + new Vector3(worldX, worldY, worldZ);
                contour.Add(worldPos);

                // Find next edge pixel in 8-neighborhood
                Vector2Int next = current;
                bool found = false;

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;

                        int nx = current.x + dx;
                        int ny = current.y + dy;

                        if (nx >= 0 && nx < width && ny >= 0 && ny < height &&
                            isEdge[nx, ny] && !visited[nx, ny])
                        {
                            next = new Vector2Int(nx, ny);
                            found = true;
                            break;
                        }
                    }
                    if (found) break;
                }

                if (!found)
                    break;

                current = next;
            }

            if (contour.Count > 2)
            {
                boundaryContours.Add(contour);
            }
        }
    }

    float[,] ComputeDistanceTransform(bool[,] textureMap, int width, int height)
    {
        float[,] distance = new float[width, height];

        // Initialize distance map
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                distance[x, y] = textureMap[x, y] ? 99999f : 0f;
            }
        }

        // Forward pass
        for (int y = 1; y < height; y++)
        {
            for (int x = 1; x < width; x++)
            {
                if (textureMap[x, y])
                {
                    float minDist = distance[x, y];
                    minDist = Mathf.Min(minDist, distance[x - 1, y] + 1);
                    minDist = Mathf.Min(minDist, distance[x, y - 1] + 1);
                    minDist = Mathf.Min(minDist, distance[x - 1, y - 1] + 1.414f);
                    distance[x, y] = minDist;
                }
            }
        }

        // Backward pass
        for (int y = height - 2; y >= 0; y--)
        {
            for (int x = width - 2; x >= 0; x--)
            {
                if (textureMap[x, y])
                {
                    float minDist = distance[x, y];
                    minDist = Mathf.Min(minDist, distance[x + 1, y] + 1);
                    minDist = Mathf.Min(minDist, distance[x, y + 1] + 1);
                    minDist = Mathf.Min(minDist, distance[x + 1, y + 1] + 1.414f);
                    distance[x, y] = minDist;
                }
            }
        }

        return distance;
    }

    void ApplyTextureExpansion()
    {
        if (terrain == null)
        {
            Debug.LogError("No terrain assigned!");
            return;
        }

        TerrainData terrainData = terrain.terrainData;
        int alphaWidth = terrainData.alphamapWidth;
        int alphaHeight = terrainData.alphamapHeight;

        // Create backup before making changes
        CreateBackup();

        float[,,] alphaMaps = terrainData.GetAlphamaps(0, 0, alphaWidth, alphaHeight);

        if (replaceWithinLoops)
        {
            Debug.Log("Replacing texture within boundary loops only...");
            ApplyTextureWithinLoops(alphaMaps, terrainData);
        }
        else
        {
            Debug.Log("Starting texture expansion by " + expansionDistance + " meters...");
            ApplyTextureExpansionStandard(alphaMaps, terrainData);
        }

        terrainData.SetAlphamaps(0, 0, alphaMaps);

        Debug.Log("Texture operation complete! Re-run detection to see new boundaries.");

        EditorUtility.SetDirty(terrainData);
    }

    void ApplyTextureWithinLoops(float[,,] alphaMaps, TerrainData terrainData)
    {
        int alphaWidth = terrainData.alphamapWidth;
        int alphaHeight = terrainData.alphamapHeight;

        Debug.Log("Painting with Texture " + replacementTextureIndex + " at " + paintOpacity + "% opacity");

        // Create a binary map of the selected texture
        bool[,] textureMap = new bool[alphaWidth, alphaHeight];

        for (int y = 0; y < alphaHeight; y++)
        {
            for (int x = 0; x < alphaWidth; x++)
            {
                textureMap[x, y] = alphaMaps[y, x, selectedTextureIndex] > 0.5f;
            }
        }

        // Calculate opacity multiplier
        float opacityMultiplier = paintOpacity / 100f;

        // Apply the replacement texture only within the loops
        for (int y = 0; y < alphaHeight; y++)
        {
            for (int x = 0; x < alphaWidth; x++)
            {
                if (textureMap[x, y])
                {
                    if (!IsWithinBox(x, y, terrainData))
                        continue;
                    float paintAmount = opacityMultiplier;

                    // Calculate how much to reduce from other textures
                    float totalToReduce = paintAmount;

                    // Reduce from the original texture first
                    if (selectedTextureIndex != replacementTextureIndex)
                    {
                        float originalAmount = alphaMaps[y, x, selectedTextureIndex];
                        float reduceFromOriginal = Mathf.Min(originalAmount, totalToReduce);
                        alphaMaps[y, x, selectedTextureIndex] -= reduceFromOriginal;
                        totalToReduce -= reduceFromOriginal;
                    }

                    // Reduce remaining amount from other textures proportionally
                    if (totalToReduce > 0f)
                    {
                        float totalOther = 0f;
                        for (int layer = 0; layer < terrainData.alphamapLayers; layer++)
                        {
                            if (layer != replacementTextureIndex && layer != selectedTextureIndex)
                            {
                                totalOther += alphaMaps[y, x, layer];
                            }
                        }

                        if (totalOther > 0f)
                        {
                            for (int layer = 0; layer < terrainData.alphamapLayers; layer++)
                            {
                                if (layer != replacementTextureIndex && layer != selectedTextureIndex)
                                {
                                    float proportion = alphaMaps[y, x, layer] / totalOther;
                                    float reduction = totalToReduce * proportion;
                                    alphaMaps[y, x, layer] -= reduction;
                                    alphaMaps[y, x, layer] = Mathf.Max(0f, alphaMaps[y, x, layer]);
                                }
                            }
                        }
                    }

                    // Add to replacement texture
                    alphaMaps[y, x, replacementTextureIndex] += paintAmount;
                }
            }
        }

        // Normalize so all texture weights sum to 1
        for (int y = 0; y < alphaHeight; y++)
        {
            for (int x = 0; x < alphaWidth; x++)
            {
                float sum = 0f;
                for (int layer = 0; layer < terrainData.alphamapLayers; layer++)
                {
                    sum += alphaMaps[y, x, layer];
                }

                if (sum > 0f)
                {
                    for (int layer = 0; layer < terrainData.alphamapLayers; layer++)
                    {
                        alphaMaps[y, x, layer] /= sum;
                    }
                }
            }
        }

        terrainData.SetAlphamaps(0, 0, alphaMaps);

        Debug.Log("Texture expansion complete! Re-run detection to see new boundaries.");

        EditorUtility.SetDirty(terrainData);
    }

    void ApplyTextureExpansionStandard(float[,,] alphaMaps, TerrainData terrainData)
    {
        int alphaWidth = terrainData.alphamapWidth;
        int alphaHeight = terrainData.alphamapHeight;

        Debug.Log("Painting with Texture " + replacementTextureIndex + " at " + paintOpacity + "% opacity");
        Debug.Log("Replace original texture: " + replaceOriginalTexture);

        // Convert expansion distance from world units to alphamap pixels
        float pixelsPerMeterX = alphaWidth / terrainData.size.x;
        float pixelsPerMeterZ = alphaHeight / terrainData.size.z;
        float avgPixelsPerMeter = (pixelsPerMeterX + pixelsPerMeterZ) / 2f;
        int expansionPixels = Mathf.CeilToInt(expansionDistance * avgPixelsPerMeter);

        Debug.Log("Expansion in pixels: " + expansionPixels);

        // Create a copy of the current selected texture alpha
        float[,] currentTexture = new float[alphaWidth, alphaHeight];
        for (int y = 0; y < alphaHeight; y++)
        {
            for (int x = 0; x < alphaWidth; x++)
            {
                currentTexture[x, y] = alphaMaps[y, x, selectedTextureIndex];
            }
        }

        // Create expanded mask using morphological dilation
        float[,] expandedMask = new float[alphaWidth, alphaHeight];

        for (int y = 0; y < alphaHeight; y++)
        {
            for (int x = 0; x < alphaWidth; x++)
            {
                float maxValue = currentTexture[x, y];

                // Check circular neighborhood
                for (int dy = -expansionPixels; dy <= expansionPixels; dy++)
                {
                    for (int dx = -expansionPixels; dx <= expansionPixels; dx++)
                    {
                        // Check if within circular radius
                        if (dx * dx + dy * dy > expansionPixels * expansionPixels)
                            continue;

                        int nx = x + dx;
                        int ny = y + dy;

                        if (nx >= 0 && nx < alphaWidth && ny >= 0 && ny < alphaHeight)
                        {
                            if (currentTexture[nx, ny] > maxValue)
                            {
                                maxValue = currentTexture[nx, ny];
                            }
                        }
                    }
                }

                expandedMask[x, y] = maxValue;
            }
        }

        // Calculate opacity multiplier (1-100% to 0.01-1.0)
        float opacityMultiplier = paintOpacity / 100f;

        // Apply the replacement texture
        for (int y = 0; y < alphaHeight; y++)
        {
            for (int x = 0; x < alphaWidth; x++)
            {
                bool shouldPaint = false;
                float paintAmount = 0f;

                if (replaceOriginalTexture)
                {
                    // Paint everywhere the expanded mask covers (original + expansion)
                    if (expandedMask[x, y] > 0.1f)
                    {
                        shouldPaint = true;
                        paintAmount = expandedMask[x, y] * opacityMultiplier;
                    }
                }
                else
                {
                    // Only paint the newly expanded area
                    float expansionAmount = Mathf.Max(0f, expandedMask[x, y] - currentTexture[x, y]);
                    if (expansionAmount > 0f)
                    {
                        shouldPaint = true;
                        paintAmount = expansionAmount * opacityMultiplier;
                    }
                }

                if (shouldPaint)
                {
                    if (!IsWithinBox(x, y, terrainData))
                        continue;

                    // Calculate how much to reduce from other textures
                    float totalToReduce = paintAmount;

                    // If replacing original, reduce from the original texture first
                    if (replaceOriginalTexture && selectedTextureIndex != replacementTextureIndex)
                    {
                        float originalAmount = alphaMaps[y, x, selectedTextureIndex];
                        float reduceFromOriginal = Mathf.Min(originalAmount, totalToReduce);
                        alphaMaps[y, x, selectedTextureIndex] -= reduceFromOriginal;
                        totalToReduce -= reduceFromOriginal;
                    }

                    // Reduce remaining amount from other textures proportionally
                    if (totalToReduce > 0f)
                    {
                        float totalOther = 0f;
                        for (int layer = 0; layer < terrainData.alphamapLayers; layer++)
                        {
                            if (layer != replacementTextureIndex && layer != selectedTextureIndex)
                            {
                                totalOther += alphaMaps[y, x, layer];
                            }
                        }

                        if (totalOther > 0f)
                        {
                            for (int layer = 0; layer < terrainData.alphamapLayers; layer++)
                            {
                                if (layer != replacementTextureIndex && layer != selectedTextureIndex)
                                {
                                    float proportion = alphaMaps[y, x, layer] / totalOther;
                                    float reduction = totalToReduce * proportion;
                                    alphaMaps[y, x, layer] -= reduction;
                                    alphaMaps[y, x, layer] = Mathf.Max(0f, alphaMaps[y, x, layer]);
                                }
                            }
                        }
                    }

                    // Add to replacement texture
                    alphaMaps[y, x, replacementTextureIndex] += paintAmount;
                }
            }
        }

        // Normalize so all texture weights sum to 1
        for (int y = 0; y < alphaHeight; y++)
        {
            for (int x = 0; x < alphaWidth; x++)
            {
                float sum = 0f;
                for (int layer = 0; layer < terrainData.alphamapLayers; layer++)
                {
                    sum += alphaMaps[y, x, layer];
                }

                if (sum > 0f)
                {
                    for (int layer = 0; layer < terrainData.alphamapLayers; layer++)
                    {
                        alphaMaps[y, x, layer] /= sum;
                    }
                }
            }
        }

        terrainData.SetAlphamaps(0, 0, alphaMaps);

        Debug.Log("Texture expansion complete! Re-run detection to see new boundaries.");

        EditorUtility.SetDirty(terrainData);
    }

    bool IsWithinBox(int x, int y, TerrainData terrainData)
    {
        if (!useBoxConstraint || centerObject == null)
            return true;

        float worldX = (float)x / terrainData.alphamapWidth * terrainData.size.x;
        float worldZ = (float)y / terrainData.alphamapHeight * terrainData.size.z;
        Vector3 worldPos = terrain.transform.position + new Vector3(worldX, 0, worldZ);
        
        Vector3 centerPos = centerObject.transform.position;
        float halfSizeX = boxSizeX / 2f;
        float halfSizeZ = boxSizeZ / 2f;
        
        return worldPos.x >= centerPos.x - halfSizeX &&
            worldPos.x <= centerPos.x + halfSizeX &&
            worldPos.z >= centerPos.z - halfSizeZ &&
            worldPos.z <= centerPos.z + halfSizeZ;
    }

    void CreateBackup()
    {
        if (terrain == null) return;

        TerrainData terrainData = terrain.terrainData;
        int alphaWidth = terrainData.alphamapWidth;
        int alphaHeight = terrainData.alphamapHeight;
        int layers = terrainData.alphamapLayers;

        // Create a deep copy of the alphamaps
        float[,,] currentAlphamaps = terrainData.GetAlphamaps(0, 0, alphaWidth, alphaHeight);
        backupAlphamaps = new float[alphaHeight, alphaWidth, layers];

        for (int y = 0; y < alphaHeight; y++)
        {
            for (int x = 0; x < alphaWidth; x++)
            {
                for (int layer = 0; layer < layers; layer++)
                {
                    backupAlphamaps[y, x, layer] = currentAlphamaps[y, x, layer];
                }
            }
        }

        hasBackup = true;
        Debug.Log("Backup created successfully.");
    }

    void RestoreBackup()
    {
        if (terrain == null)
        {
            Debug.LogError("No terrain assigned!");
            return;
        }

        if (!hasBackup || backupAlphamaps == null)
        {
            Debug.LogError("No backup available to restore!");
            return;
        }

        if (EditorUtility.DisplayDialog("Restore Backup",
            "This will restore the terrain textures to the state before the last expansion. Continue?",
            "Restore", "Cancel"))
        {
            TerrainData terrainData = terrain.terrainData;
            terrainData.SetAlphamaps(0, 0, backupAlphamaps);

            Debug.Log("Backup restored successfully!");
            EditorUtility.SetDirty(terrainData);
            SceneView.RepaintAll();

            // Clear the backup after restoring
            backupAlphamaps = null;
            hasBackup = false;
        }
    }

    void FloodFill(bool[,] textureMap, bool[,] visited, int x, int y, int width, int height)
    {
        Stack<Vector2Int> stack = new Stack<Vector2Int>();
        stack.Push(new Vector2Int(x, y));

        while (stack.Count > 0)
        {
            Vector2Int pos = stack.Pop();
            int px = pos.x;
            int py = pos.y;

            if (px < 0 || px >= width || py < 0 || py >= height)
                continue;

            if (visited[px, py] || !textureMap[px, py])
                continue;

            visited[px, py] = true;

            stack.Push(new Vector2Int(px + 1, py));
            stack.Push(new Vector2Int(px - 1, py));
            stack.Push(new Vector2Int(px, py + 1));
            stack.Push(new Vector2Int(px, py - 1));
        }
    }

    void OnSceneGUI(SceneView sceneView)
    {
        // Draw expansion preview
        if (previewExpansion && boundaryPoints.Count > 0)
        {
            Handles.color = new Color(1f, 1f, 0f, 0.5f); // Yellow semi-transparent

            if (replaceWithinLoops)
            {
                // Show preview only within the loops
                TerrainData terrainData = terrain.terrainData;
                int alphaWidth = terrainData.alphamapWidth;
                int alphaHeight = terrainData.alphamapHeight;
                float[,,] alphaMaps = terrainData.GetAlphamaps(0, 0, alphaWidth, alphaHeight);

                // Create a binary map of the selected texture
                bool[,] textureMap = new bool[alphaWidth, alphaHeight];

                for (int y = 0; y < alphaHeight; y++)
                {
                    for (int x = 0; x < alphaWidth; x++)
                    {
                        textureMap[x, y] = alphaMaps[y, x, selectedTextureIndex] > 0.5f;
                    }
                }

                // Draw small discs at each point within the loops
                for (int y = 0; y < alphaHeight; y += 5) // Sample every 5 pixels for performance
                {
                    for (int x = 0; x < alphaWidth; x += 5)
                    {
                        if (textureMap[x, y])
                        {
                            float worldX = (float)x / alphaWidth * terrainData.size.x;
                            float worldZ = (float)y / alphaHeight * terrainData.size.z;
                            float worldY = terrain.SampleHeight(terrain.transform.position + new Vector3(worldX, 0, worldZ));
                            Vector3 worldPos = terrain.transform.position + new Vector3(worldX, worldY, worldZ);

                            Handles.DrawSolidDisc(worldPos, Vector3.up, 0.3f);
                        }
                    }
                }
            }
            else
            {
                // Show expansion distance circles
                foreach (Vector3 point in boundaryPoints)
                {
                    Handles.DrawWireDisc(point, Vector3.up, expansionDistance);
                }
            }
        }

        // Draw boundary points
        if (showBoundaries && boundaryPoints.Count > 0)
        {
            Handles.color = boundaryColor;
            foreach (Vector3 point in boundaryPoints)
            {
                Handles.DrawSolidDisc(point, Vector3.up, 0.2f);
            }
        }

        // Draw boundary contour lines
        if (showBoundaryLines && boundaryContours.Count > 0)
        {
            Handles.color = boundaryColor;

            foreach (List<Vector3> contour in boundaryContours)
            {
                if (contour.Count < 2)
                    continue;

                for (int i = 0; i < contour.Count - 1; i++)
                {
                    if (Vector3.Distance(contour[i], contour[i + 1]) <= boundaryConnectionDistance)
                    {
                        Handles.DrawLine(contour[i], contour[i + 1]);
                    }
                }

                if (contour.Count > 2)
                {
                    if (Vector3.Distance(contour[contour.Count - 1], contour[0]) <= boundaryConnectionDistance)
                    {
                        Handles.DrawLine(contour[contour.Count - 1], contour[0]);
                    }
                }
            }
        }

        // Draw box constraint
        if (useBoxConstraint && centerObject != null)
        {
            Vector3 centerPos = centerObject.transform.position;
            float halfSizeX = boxSizeX / 2f;
            float halfSizeZ = boxSizeZ / 2f;
            
            Vector3[] corners = new Vector3[4];
            corners[0] = new Vector3(centerPos.x - halfSizeX, centerPos.y, centerPos.z - halfSizeZ);
            corners[1] = new Vector3(centerPos.x + halfSizeX, centerPos.y, centerPos.z - halfSizeZ);
            corners[2] = new Vector3(centerPos.x + halfSizeX, centerPos.y, centerPos.z + halfSizeZ);
            corners[3] = new Vector3(centerPos.x - halfSizeX, centerPos.y, centerPos.z + halfSizeZ);
            
            Handles.color = Color.blue;
            Handles.DrawLine(corners[0], corners[1]);
            Handles.DrawLine(corners[1], corners[2]);
            Handles.DrawLine(corners[2], corners[3]);
            Handles.DrawLine(corners[3], corners[0]);
        }
    }
}
