// USAR extension to RubbleSim (not part of the original MITLL release).
//
// Confined-space void generation for Urban Search & Rescue scenarios.
//
// Gravity-settled debris alone rarely leaves the connected cavities where victims
// survive and where a search robot can enter. This component seeds STATIC void
// obstacles (spheres, or horizontal capsule "tunnels") near the base of the pile
// BEFORE any debris is dropped. Debris settles around them; once the pile is frozen
// the obstacle colliders are removed, leaving stable cavities. Each void's location
// and size is recorded as ground truth (JSON, next to the STL export) for victim
// placement and confined-space mapping evaluation.
//
// Driven by DebrisSpawner: SpawnVoidSeeds() before piling, RevealVoids() after freeze.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class VoidSeeder : MonoBehaviour
{
    [Serializable]
    public struct GroundTruthVoid
    {
        public float x, y, z;   // world-space center
        public float radius;    // approximate cavity radius (m)
        public float length;    // capsule length (0 for spheres)
        public string shape;    // "sphere" | "capsule"
    }

    [Serializable]
    private class VoidGroundTruthFile
    {
        public int seed;
        public GroundTruthVoid[] voids;
    }

    [Header("Void generation (each field is overridable via CLI args)")]
    public bool voidsEnabled = true;      // -voidenable      (1/0)
    public int numVoids = 6;              // -numvoids
    public float voidRadiusMin = 0.5f;    // -voidsizemin     (m)
    public float voidRadiusMax = 1.2f;    // -voidsizemax     (m)
    public float voidBandHeight = 2.5f;   // -voidbandheight  seed centers sit from ground up to this height
    public float voidCoreFraction = 0.7f; // -voidcorefrac    shrink footprint so voids land in the pile core
    public int voidShape = 0;             // -voidshape       0 = sphere, 1 = horizontal capsule (tunnel)
    public float groundY = 0f;            //                  plane the pile rests on
    public bool exportGroundTruth = false;// -exportvoids     (defaults to follow -exportstl)

    private RandomManager random;
    private readonly List<GameObject> seeds = new List<GameObject>();
    private readonly List<GroundTruthVoid> voids = new List<GroundTruthVoid>();
    private Transform seedRoot;
    private bool argsLoaded = false;

    private void LoadArgs(bool exportStlFallback)
    {
        if (argsLoaded) return;
        voidsEnabled      = CustomArgs.FloatToBool(CustomArgs.GetWithDefault("voidenable", voidsEnabled ? 1 : 0));
        numVoids          = (int)CustomArgs.GetWithDefault("numvoids", numVoids);
        voidRadiusMin     = CustomArgs.GetWithDefault("voidsizemin", voidRadiusMin);
        voidRadiusMax     = CustomArgs.GetWithDefault("voidsizemax", voidRadiusMax);
        voidBandHeight    = CustomArgs.GetWithDefault("voidbandheight", voidBandHeight);
        voidCoreFraction  = CustomArgs.GetWithDefault("voidcorefrac", voidCoreFraction);
        voidShape         = (int)CustomArgs.GetWithDefault("voidshape", voidShape);
        exportGroundTruth = CustomArgs.FloatToBool(CustomArgs.GetWithDefault("exportvoids", exportStlFallback ? 1 : 0));
        argsLoaded = true;
    }

    // Called by DebrisSpawner before any debris is dropped, with the debris spawn bounds.
    public void SpawnVoidSeeds(Bounds spawnBounds, bool exportStlFallback)
    {
        LoadArgs(exportStlFallback);
        if (!voidsEnabled || numVoids <= 0) return;

        random = RandomManager.Instance;
        ClearSeeds();
        seedRoot = new GameObject("VoidSeeds").transform;

        // Debris falls from the spawn volume and piles up on the ground within roughly the
        // same x/z footprint. Place voids in the core of that footprint, near the base, so
        // later debris layers bury them.
        Vector3 c = spawnBounds.center;
        float halfX = spawnBounds.extents.x * voidCoreFraction;
        float halfZ = spawnBounds.extents.z * voidCoreFraction;

        for (int i = 0; i < numVoids; i++)
        {
            float radius = random.GetStaticFloat(voidRadiusMin, voidRadiusMax);
            float x = c.x + random.GetStaticFloat(-halfX, halfX);
            float z = c.z + random.GetStaticFloat(-halfZ, halfZ);
            float y = groundY + random.GetStaticFloat(radius, radius + voidBandHeight);

            bool capsule = voidShape == 1;
            GameObject seed = GameObject.CreatePrimitive(capsule ? PrimitiveType.Capsule
                                                                 : PrimitiveType.Sphere);
            seed.name = "VoidSeed_" + i;
            seed.transform.SetParent(seedRoot);
            seed.transform.position = new Vector3(x, y, z);

            float length = 0f;
            if (capsule)
            {
                // Lay the capsule on its side (random yaw) to carve a horizontal tunnel.
                // Unity's capsule primitive is 2 units tall / 1 unit diameter at unit scale.
                length = radius * random.GetStaticFloat(2f, 4f);
                seed.transform.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
                seed.transform.rotation = Quaternion.Euler(90f, random.GetStaticFloat(0f, 180f), 0f);
            }
            else
            {
                seed.transform.localScale = Vector3.one * (radius * 2f);
            }

            // No Rigidbody: the seed is a STATIC collider, so it holds position while debris
            // piles around it instead of falling.
            seeds.Add(seed);
            voids.Add(new GroundTruthVoid
            {
                x = x, y = y, z = z, radius = radius, length = length,
                shape = capsule ? "capsule" : "sphere"
            });
        }
        Debug.Log($"[VoidSeeder] Seeded {seeds.Count} void obstacle(s).");
    }

    // Called by DebrisSpawner after the pile is frozen: strip the seed colliders/meshes
    // (leaving cavities) and turn each seed into a lightweight ground-truth marker.
    public void RevealVoids()
    {
        if (seeds.Count == 0) return;

        for (int i = 0; i < seeds.Count; i++)
        {
            GameObject seed = seeds[i];
            if (seed == null) continue;

            Collider col = seed.GetComponent<Collider>();
            if (col != null) Destroy(col);
            MeshRenderer mr = seed.GetComponent<MeshRenderer>();
            if (mr != null) mr.enabled = false;
            MeshFilter mf = seed.GetComponent<MeshFilter>();
            if (mf != null) Destroy(mf);

            GroundTruthVoid v = voids[i];
            VoidMarker marker = seed.AddComponent<VoidMarker>();
            marker.radius = v.radius;
            marker.isCapsule = v.shape == "capsule";
            seed.name = "Void_" + i;
        }
        if (seedRoot != null) seedRoot.name = "Voids";

        Debug.Log($"[VoidSeeder] Revealed {seeds.Count} void(s) in the pile.");
        if (exportGroundTruth) ExportGroundTruth();
    }

    public void ClearSeeds()
    {
        foreach (var s in seeds)
        {
            if (s != null) Destroy(s);
        }
        seeds.Clear();
        voids.Clear();
        if (seedRoot != null) Destroy(seedRoot.gameObject);
    }

    // Ground-truth void locations for downstream use (victim placement, evaluation).
    public List<GroundTruthVoid> GetVoids() { return voids; }

    private void ExportGroundTruth()
    {
        string folder = Path.Combine(Application.persistentDataPath, "SceneData");
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, $"Voids_{DateTime.Now:yy-MM-dd-HH-mm}.json");

        var payload = new VoidGroundTruthFile
        {
            seed = random != null ? random.seed : 0,
            voids = voids.ToArray()
        };
        File.WriteAllText(file, JsonUtility.ToJson(payload, true));
        Debug.Log("[VoidSeeder] Void ground truth written to " + file);
    }
}
