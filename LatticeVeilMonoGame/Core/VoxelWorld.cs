using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace LatticeVeilMonoGame.Core;

public sealed class VoxelWorld
{
    private static readonly Regex ChunkNameRegex = new(@"chunk_(?<x>-?\d+)_(?<y>-?\d+)_(?<z>-?\d+)\.bin", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly Dictionary<ChunkCoord, VoxelChunkData> _chunks = new();
    private readonly object _chunksLock = new();
    private readonly Logger _log;
    private readonly int _maxChunkY;

    public WorldMeta Meta { get; }
    public string WorldPath { get; }
    public string ChunksDir { get; }

    public VoxelWorld(WorldMeta meta, string worldPath, Logger log)
    {
        Meta = meta;
        WorldPath = worldPath;
        ChunksDir = Path.Combine(worldPath, "chunks");
        _log = log;

        // Backward compatibility: repair legacy metadata with missing/zero world sizes.
        Meta.Size ??= new WorldSize();
        if (Meta.Size.Width <= 0)
            Meta.Size.Width = 512;
        if (Meta.Size.Depth <= 0)
            Meta.Size.Depth = 512;
        if (Meta.Size.Height <= 0)
            Meta.Size.Height = VoxelChunkData.ChunkSizeY;

        var height = Math.Max(1, Meta.Size.Height);
        _maxChunkY = (height - 1) / VoxelChunkData.ChunkSizeY;
    }

    public static VoxelWorld? Load(string worldPath, string metaPath, Logger log)
    {
        var meta = WorldMeta.Load(metaPath, log);
        if (meta == null)
            return null;

        var world = new VoxelWorld(meta, worldPath, log);
        world.LoadChunks();
        return world;
    }

    public List<VoxelChunkData> AllChunks() 
    { 
        lock (_chunksLock) 
            return _chunks.Values.ToList(); 
    }

    public int ChunkCount
    {
        get
        {
            lock (_chunksLock)
                return _chunks.Count;
        }
    }

    public void CopyChunksTo(List<VoxelChunkData> destination)
    {
        if (destination == null)
            throw new ArgumentNullException(nameof(destination));

        lock (_chunksLock)
        {
            destination.Clear();
            destination.AddRange(_chunks.Values);
        }
    }

    public bool TryGetChunk(ChunkCoord coord, out VoxelChunkData? chunk) 
    { 
        lock (_chunksLock) 
            return _chunks.TryGetValue(coord, out chunk); 
    }

    public void AddChunkDirect(ChunkCoord coord, VoxelChunkData chunk)
    {
        lock (_chunksLock)
            _chunks[coord] = chunk;
    }

    public int MaxChunkY => _maxChunkY;

    public byte GetBlock(int wx, int wy, int wz)
    {
        if (!IsWithinWorld(wx, wy, wz))
            return BlockIds.Air;

        var coord = WorldToChunk(wx, wy, wz, out var lx, out var ly, out var lz);
        if (!IsChunkYInRange(coord.Y))
            return BlockIds.Air;

        lock (_chunksLock)
        {
            if (_chunks.TryGetValue(coord, out var chunk))
                return chunk.GetLocal(lx, ly, lz);
        }

        // Read path must stay non-generating to avoid heavy stalls while meshing/collision query neighbors.
        return BlockIds.Air;
    }

    public bool SetBlock(int wx, int wy, int wz, byte id)
    {
        if (!IsWithinWorld(wx, wy, wz))
            return false;

        var coord = WorldToChunk(wx, wy, wz, out var lx, out var ly, out var lz);
        if (!IsChunkYInRange(coord.Y))
            return false;

        var chunk = GetOrCreateChunk(coord);
        chunk.SetLocal(lx, ly, lz, id);
        ChunkSeamRegistry.Register(coord, ChunkSurfaceProfile.FromChunk(chunk));
        MarkNeighborDirty(coord, lx, ly, lz);
        return true;
    }

    private void MarkNeighborDirty(ChunkCoord coord, int lx, int ly, int lz)
    {
        if (lx == 0) MarkDirty(new ChunkCoord(coord.X - 1, coord.Y, coord.Z));
        if (lx == VoxelChunkData.ChunkSizeX - 1) MarkDirty(new ChunkCoord(coord.X + 1, coord.Y, coord.Z));
        if (ly == 0) MarkDirty(new ChunkCoord(coord.X, coord.Y - 1, coord.Z));
        if (ly == VoxelChunkData.ChunkSizeY - 1) MarkDirty(new ChunkCoord(coord.X, coord.Y + 1, coord.Z));
        if (lz == 0) MarkDirty(new ChunkCoord(coord.X, coord.Y, coord.Z - 1));
        if (lz == VoxelChunkData.ChunkSizeZ - 1) MarkDirty(new ChunkCoord(coord.X, coord.Y, coord.Z + 1));
    }

    private void MarkDirty(ChunkCoord coord)
    {
        lock (_chunksLock)
        {
            if (_chunks.TryGetValue(coord, out var chunk))
                chunk.IsDirty = true;
        }
    }

    public void MarkAllChunksDirty()
    {
        lock (_chunksLock)
        {
            foreach (var chunk in _chunks.Values)
                chunk.IsDirty = true;
        }
    }

    public void SaveModifiedChunks()
    {
        if (!Directory.Exists(ChunksDir))
            Directory.CreateDirectory(ChunksDir);

        var chunksToSave = new List<VoxelChunkData>();
        
        // Collect chunks that need saving
        lock (_chunksLock)
        {
            foreach (var chunk in _chunks.Values)
            {
                if (chunk.NeedsSave)
                {
                    chunksToSave.Add(chunk);
                }
            }
        }

        // Limit saves to prevent freezing (max 50 chunks at once)
        const int maxChunksPerSave = 50;
        var savedCount = 0;
        
        foreach (var chunk in chunksToSave)
        {
            if (savedCount >= maxChunksPerSave)
            {
                // Mark remaining chunks as still needing save for next time
                break;
            }
            
            try
            {
                var path = Path.Combine(ChunksDir, $"chunk_{chunk.Coord.X}_{chunk.Coord.Y}_{chunk.Coord.Z}.bin");
                chunk.Save(path);
                savedCount++;
            }
            catch (Exception ex)
            {
                // Log error but continue saving other chunks
                System.Diagnostics.Debug.WriteLine($"Failed to save chunk {chunk.Coord}: {ex.Message}");
            }
        }
    }

    public void SaveChunk(ChunkCoord coord)
    {
        VoxelChunkData? chunk;
        lock (_chunksLock)
        {
            if (!_chunks.TryGetValue(coord, out chunk))
                return;
        }
        
        var path = Path.Combine(ChunksDir, $"chunk_{coord.X}_{coord.Y}_{coord.Z}.bin");
        chunk.Save(path);
    }

    public bool IsWithinWorld(int wx, int wy, int wz)
    {
        if (wy < 0)
            return false;
        return wy < Meta.Size.Height;
    }

    public VoxelChunkData GetOrCreateChunk(ChunkCoord coord)
    {
        lock (_chunksLock)
        {
            if (_chunks.TryGetValue(coord, out var existing))
                return existing;
        }

        var loaded = TryLoadChunk(coord);
        if (loaded != null)
        {
            lock (_chunksLock)
                _chunks[coord] = loaded;
            return loaded;
        }

        var chunk = new VoxelChunkData(coord);
        GenerateDefaultTerrain(chunk);
        chunk.IsDirty = true;
        
        lock (_chunksLock)
            _chunks[coord] = chunk;
        ChunkSeamRegistry.Register(coord, ChunkSurfaceProfile.FromChunk(chunk));
        return chunk;
    }

    private void GenerateDefaultTerrain(VoxelChunkData chunk)
    {
        // Deterministic fallback terrain used when no chunk file exists.
        // Generated per chunk-section to keep logic cache-friendly while allowing richer features.
        var originX = chunk.Coord.X * VoxelChunkData.ChunkSizeX;
        var originY = chunk.Coord.Y * VoxelChunkData.ChunkSizeY;
        var maxHeight = Math.Max(1, Meta.Size.Height - 1);
        const int seaLevel = 42;
        const int sectionHeight = 16;
        var originZ = chunk.Coord.Z * VoxelChunkData.ChunkSizeZ;
        var surfaceHeights = new int[VoxelChunkData.ChunkSizeX, VoxelChunkData.ChunkSizeZ];
        var poolLevels = new int[VoxelChunkData.ChunkSizeX, VoxelChunkData.ChunkSizeZ];
        var desertWeights = new float[VoxelChunkData.ChunkSizeX, VoxelChunkData.ChunkSizeZ];
        var oceanWeights = new float[VoxelChunkData.ChunkSizeX, VoxelChunkData.ChunkSizeZ];
        var blocks = new byte[VoxelChunkData.ChunkVolume];
        var seed = Meta.Seed == 0 ? 1337 : Meta.Seed;

        for (var lx = 0; lx < VoxelChunkData.ChunkSizeX; lx++)
        {
            for (var lz = 0; lz < VoxelChunkData.ChunkSizeZ; lz++)
            {
                var wx = originX + lx;
                var wz = originZ + lz;

                var desertWeight = ComputeDesertWeight(wx, wz, seed);
                var oceanWeight = ComputeOceanWeight(wx, wz, seed);
                desertWeights[lx, lz] = desertWeight;
                oceanWeights[lx, lz] = oceanWeight;

                var effectiveDesertWeight = desertWeight * (1f - oceanWeight * 0.9f);
                var surface = ComputeBaseSurfaceHeight(wx, wz, effectiveDesertWeight, oceanWeight, seed, maxHeight, seaLevel);
                var poolDepth = ComputePoolDepth(wx, wz, surface, effectiveDesertWeight, seed, seaLevel);
                var carvedSurface = Math.Clamp(surface - poolDepth, 8, maxHeight);
                var oceanColumn = oceanWeight >= 0.56f;
                var shallowWaterColumn = !oceanColumn && oceanWeight >= 0.42f && carvedSurface <= seaLevel - 1;
                if (oceanColumn || shallowWaterColumn)
                    carvedSurface = Math.Min(seaLevel - 1, carvedSurface);

                surfaceHeights[lx, lz] = carvedSurface;
                poolLevels[lx, lz] = (poolDepth > 0 && !oceanColumn && !shallowWaterColumn)
                    ? Math.Clamp(carvedSurface + 1, carvedSurface + 1, maxHeight)
                    : -1;
            }
        }

        var sectionCount = VoxelChunkData.ChunkSizeY / sectionHeight;
        for (var section = 0; section < sectionCount; section++)
        {
            var sectionStartY = section * sectionHeight;
            var sectionEndY = sectionStartY + sectionHeight;

            for (var lx = 0; lx < VoxelChunkData.ChunkSizeX; lx++)
            {
                for (var lz = 0; lz < VoxelChunkData.ChunkSizeZ; lz++)
                {
                    var wx = originX + lx;
                    var wz = originZ + lz;
                    var surface = surfaceHeights[lx, lz];
                    var poolLevel = poolLevels[lx, lz];
                    var desertWeight = desertWeights[lx, lz];
                    var oceanWeight = oceanWeights[lx, lz];
                    var oceanColumn = oceanWeight >= 0.56f && surface <= seaLevel - 1;
                    var shallowWaterColumn = !oceanColumn && oceanWeight >= 0.42f && surface <= seaLevel - 1;
                    var waterColumn = oceanColumn || shallowWaterColumn;
                    var effectiveDesertWeight = waterColumn ? 0f : desertWeight * (1f - oceanWeight * 0.9f);
                    var useDesertMaterial = !oceanColumn && effectiveDesertWeight >= 0.5f;
                    var fillerDepth = waterColumn ? 4 : (int)MathF.Round(Lerp(3f, 5f, effectiveDesertWeight));
                    // Desert and ocean columns keep a gravel sub-layer under sand before reaching stone.
                    var gravelDepth = waterColumn
                        ? 3
                        : (useDesertMaterial ? 2 + (int)MathF.Round(Lerp(0f, 3f, effectiveDesertWeight)) : 0);
                    var waterTopY = waterColumn
                        ? seaLevel
                        : (poolLevel >= 0 ? poolLevel : -1);

                    for (var ly = sectionStartY; ly < sectionEndY; ly++)
                    {
                        var wy = originY + ly;
                        byte block;

                        if (wy > maxHeight)
                        {
                            block = BlockIds.Air;
                        }
                        else if (wy == 0)
                        {
                            block = BlockIds.Nullblock;
                        }
                        else if (wy > surface)
                        {
                            if (wy == waterTopY)
                                block = BlockIds.Water;
                            else
                                block = BlockIds.Air;
                        }
                        else if (wy == surface)
                        {
                            block = (useDesertMaterial || waterColumn) ? BlockIds.Sand : BlockIds.Grass;
                        }
                        else if (wy >= surface - fillerDepth)
                        {
                            block = (useDesertMaterial || waterColumn) ? BlockIds.Sand : BlockIds.Dirt;
                        }
                        else if ((useDesertMaterial || waterColumn) && wy >= surface - fillerDepth - gravelDepth)
                        {
                            block = BlockIds.Gravel;
                        }
                        else
                        {
                            block = BlockIds.Stone;
                        }

                        if (block != BlockIds.Air
                            && block != BlockIds.Water
                            && ShouldCarveCave(wx, wy, wz, surface, seed))
                        {
                            block = BlockIds.Air;
                        }

                        var index = ((lx * VoxelChunkData.ChunkSizeY) + ly) * VoxelChunkData.ChunkSizeZ + lz;
                        blocks[index] = block;
                    }
                }
            }
        }

        chunk.Load(blocks);
    }

    private static float ComputeDesertWeight(int wx, int wz, int seed)
    {
        // Blend wide climate regions with rare pockets so grasslands remain dominant.
        const int smoothOffset = 20;
        var center = ComputeRawDesertClimateSignal(wx, wz, seed);
        var north = ComputeRawDesertClimateSignal(wx, wz - smoothOffset, seed);
        var south = ComputeRawDesertClimateSignal(wx, wz + smoothOffset, seed);
        var east = ComputeRawDesertClimateSignal(wx + smoothOffset, wz, seed);
        var west = ComputeRawDesertClimateSignal(wx - smoothOffset, wz, seed);
        var climate = center * 0.44f + (north + south + east + west) * 0.14f;

        // Macro bands produce medium-to-large deserts, but with reduced overall frequency.
        var macroWeight = ComputeBandWeight(climate, center: 0.33f, halfWidth: 0.19f);

        // Rare medium pockets create non-uniform outlines and occasional mid-size islands.
        var mediumPocketNoise = FractalValueNoise2D(wx * 0.0029f, wz * 0.0029f, seed + 941, octaves: 2, lacunarity: 2.0f, persistence: 0.5f);
        var mediumPocketWeight = ComputeBandWeight(mediumPocketNoise, center: 0.67f, halfWidth: 0.09f) * 0.72f;

        // Very small pockets can still appear, but are intentionally uncommon.
        var smallPocketNoise = FractalValueNoise2D(wx * 0.0064f, wz * 0.0064f, seed + 2141, octaves: 2, lacunarity: 2.0f, persistence: 0.53f);
        var smallPocketWeight = ComputeBandWeight(smallPocketNoise, center: 0.76f, halfWidth: 0.07f) * 0.58f;

        // Wet climates suppress pockets so grasslands remain the dominant biome.
        var pocketClimateGate = ComputeBandWeight(climate, center: 0.18f, halfWidth: 0.34f);
        mediumPocketWeight *= Lerp(0.45f, 1f, pocketClimateGate);
        smallPocketWeight *= Lerp(0.35f, 0.9f, pocketClimateGate);

        var combined = MathF.Max(macroWeight, MathF.Max(mediumPocketWeight, smallPocketWeight));
        return Math.Clamp(combined, 0f, 1f);
    }

    private static float ComputeOceanWeight(int wx, int wz, int seed)
    {
        // Ocean macro signal (very low frequency) with smoothing for large contiguous bodies.
        const int smoothOffset = 28;
        var center = ComputeRawOceanClimateSignal(wx, wz, seed);
        var north = ComputeRawOceanClimateSignal(wx, wz - smoothOffset, seed);
        var south = ComputeRawOceanClimateSignal(wx, wz + smoothOffset, seed);
        var east = ComputeRawOceanClimateSignal(wx + smoothOffset, wz, seed);
        var west = ComputeRawOceanClimateSignal(wx - smoothOffset, wz, seed);
        var continental = center * 0.46f + (north + south + east + west) * 0.135f;

        // Convert to [0..1] and invert "landness" to get ocean dominance.
        var continental01 = Math.Clamp((continental + 1f) * 0.5f, 0f, 1f);
        var landness = ComputeBandWeight(continental01, center: 0.50f, halfWidth: 0.22f);
        var macroOcean = 1f - landness;

        // Coast variation adds natural inlets and jagged shorelines.
        var coastNoise = FractalValueNoise2D(wx * 0.0034f, wz * 0.0034f, seed + 1451, octaves: 2, lacunarity: 2.0f, persistence: 0.5f);
        var coast01 = Math.Clamp((coastNoise + 1f) * 0.5f, 0f, 1f);
        var coastPerturb = ComputeBandWeight(1f - coast01, center: 0.48f, halfWidth: 0.26f) * 0.24f;

        return Math.Clamp(MathF.Max(macroOcean, macroOcean * 0.82f + coastPerturb), 0f, 1f);
    }

    private static float ComputeBandWeight(float sample, float center, float halfWidth)
    {
        var range = MathF.Max(0.0001f, halfWidth * 2f);
        var t = (sample - (center - halfWidth)) / range;
        return SmoothStep(Math.Clamp(t, 0f, 1f));
    }

    private static int ComputeBaseSurfaceHeight(int wx, int wz, float desertWeight, float oceanWeight, int seed, int maxHeight, int seaLevel)
    {
        var macro = FractalValueNoise2D(wx * 0.0042f, wz * 0.0042f, seed + 911, octaves: 4, lacunarity: 2.0f, persistence: 0.52f);
        var detail = FractalValueNoise2D(wx * 0.016f, wz * 0.016f, seed + 307, octaves: 2, lacunarity: 2.1f, persistence: 0.5f);
        var dunes = FractalValueNoise2D(wx * 0.0105f, wz * 0.0105f, seed + 577, octaves: 3, lacunarity: 1.95f, persistence: 0.45f);
        var oceanRipple = FractalValueNoise2D(wx * 0.0075f, wz * 0.0075f, seed + 1301, octaves: 2, lacunarity: 2.0f, persistence: 0.5f);

        var grassyHeight = 58f + macro * 10f + detail * 3.5f;
        var desertHeight = 55f + macro * 6.5f + detail * 2.2f + dunes * 3.8f;
        var landHeight = Lerp(grassyHeight, desertHeight, desertWeight);
        var oceanFloorHeight = (seaLevel - 1f) + oceanRipple * 0.35f;
        var rawHeight = Lerp(landHeight, oceanFloorHeight, oceanWeight);

        return Math.Clamp((int)MathF.Round(rawHeight), 10, maxHeight);
    }

    private static int ComputePoolDepth(int wx, int wz, int surface, float desertWeight, int seed, int seaLevel)
    {
        if (surface <= seaLevel + 2)
            return 0;

        var broad = FractalValueNoise2D(wx * 0.021f, wz * 0.021f, seed + 4241, octaves: 2, lacunarity: 2.0f, persistence: 0.5f);
        var detail = FractalValueNoise2D(wx * 0.067f, wz * 0.067f, seed + 8819, octaves: 1, lacunarity: 2.0f, persistence: 0.5f);
        var signal = broad * 0.78f + detail * 0.22f;
        var threshold = Lerp(0.72f, 0.79f, desertWeight);

        if (signal <= threshold)
            return 0;

        var normalized = (signal - threshold) / MathF.Max(0.001f, 1f - threshold);
        var depth = 1 + (int)MathF.Round(normalized * 3f);
        return Math.Clamp(depth, 1, 4);
    }

    private static float ComputeRawDesertClimateSignal(int wx, int wz, int seed)
    {
        var climate = FractalValueNoise2D(wx * 0.00125f, wz * 0.00125f, seed + 173, octaves: 3, lacunarity: 2.02f, persistence: 0.5f);
        var warp = FractalValueNoise2D(wx * 0.0024f, wz * 0.0024f, seed + 281, octaves: 2, lacunarity: 2.0f, persistence: 0.5f) * 0.05f;
        return climate + warp;
    }

    private static float ComputeRawOceanClimateSignal(int wx, int wz, int seed)
    {
        var continental = FractalValueNoise2D(wx * 0.00085f, wz * 0.00085f, seed + 701, octaves: 3, lacunarity: 2.0f, persistence: 0.5f);
        var warp = FractalValueNoise2D(wx * 0.0017f, wz * 0.0017f, seed + 977, octaves: 2, lacunarity: 2.0f, persistence: 0.5f) * 0.08f;
        return continental + warp;
    }

    public float GetDesertWeightAt(int wx, int wz)
    {
        var seed = Meta.Seed == 0 ? 1337 : Meta.Seed;
        return ComputeDesertWeight(wx, wz, seed);
    }

    public float GetOceanWeightAt(int wx, int wz)
    {
        var seed = Meta.Seed == 0 ? 1337 : Meta.Seed;
        return ComputeOceanWeight(wx, wz, seed);
    }

    public string GetBiomeNameAt(int wx, int wz)
    {
        var oceanWeight = GetOceanWeightAt(wx, wz);
        if (oceanWeight >= 0.56f)
            return "Ocean";
        return GetDesertWeightAt(wx, wz) >= 0.5f ? "Desert" : "Grasslands";
    }

    private static bool ShouldCarveCave(int wx, int wy, int wz, int surface, int seed)
    {
        var depth = surface - wy;
        if (depth < 6 || wy <= 3)
            return false;

        // Two fields mixed together form blob-like cave pockets while remaining deterministic.
        var coarse = ValueNoise3D(wx * 0.035f, wy * 0.028f, wz * 0.035f, seed + 4099);
        var detail = ValueNoise3D(wx * 0.082f, wy * 0.071f, wz * 0.082f, seed + 8221);
        var shape = coarse * 0.74f + detail * 0.26f;

        var depthBias = Math.Clamp((depth - 12f) / 96f, 0f, 1f) * 0.16f;
        var threshold = 0.68f - depthBias;
        return shape > threshold;
    }

    private static float FractalValueNoise2D(float x, float z, int seed, int octaves, float lacunarity, float persistence)
    {
        var value = 0f;
        var amplitude = 1f;
        var frequency = 1f;
        var amplitudeSum = 0f;

        for (var i = 0; i < octaves; i++)
        {
            value += ValueNoise2D(x * frequency, z * frequency, seed + i * 1013) * amplitude;
            amplitudeSum += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }

        if (amplitudeSum <= 0f)
            return 0f;

        return value / amplitudeSum;
    }

    private static float ValueNoise2D(float x, float z, int seed)
    {
        var x0 = (int)MathF.Floor(x);
        var z0 = (int)MathF.Floor(z);
        var x1 = x0 + 1;
        var z1 = z0 + 1;

        var tx = SmoothStep(x - x0);
        var tz = SmoothStep(z - z0);

        var v00 = HashNoise2D(x0, z0, seed);
        var v10 = HashNoise2D(x1, z0, seed);
        var v01 = HashNoise2D(x0, z1, seed);
        var v11 = HashNoise2D(x1, z1, seed);

        var a = Lerp(v00, v10, tx);
        var b = Lerp(v01, v11, tx);
        return Lerp(a, b, tz);
    }

    private static float ValueNoise3D(float x, float y, float z, int seed)
    {
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var z0 = (int)MathF.Floor(z);
        var x1 = x0 + 1;
        var y1 = y0 + 1;
        var z1 = z0 + 1;

        var tx = SmoothStep(x - x0);
        var ty = SmoothStep(y - y0);
        var tz = SmoothStep(z - z0);

        var c000 = HashNoise3D(x0, y0, z0, seed);
        var c100 = HashNoise3D(x1, y0, z0, seed);
        var c010 = HashNoise3D(x0, y1, z0, seed);
        var c110 = HashNoise3D(x1, y1, z0, seed);
        var c001 = HashNoise3D(x0, y0, z1, seed);
        var c101 = HashNoise3D(x1, y0, z1, seed);
        var c011 = HashNoise3D(x0, y1, z1, seed);
        var c111 = HashNoise3D(x1, y1, z1, seed);

        var x00 = Lerp(c000, c100, tx);
        var x10 = Lerp(c010, c110, tx);
        var x01 = Lerp(c001, c101, tx);
        var x11 = Lerp(c011, c111, tx);
        var y0Lerp = Lerp(x00, x10, ty);
        var y1Lerp = Lerp(x01, x11, ty);
        return Lerp(y0Lerp, y1Lerp, tz);
    }

    private static float HashNoise2D(int x, int z, int seed)
    {
        unchecked
        {
            var h = (uint)seed;
            h ^= (uint)x * 0x9E3779B9u;
            h ^= (uint)z * 0x85EBCA6Bu;
            h ^= h >> 16;
            h *= 0x7FEB352Du;
            h ^= h >> 15;
            h *= 0x846CA68Bu;
            h ^= h >> 16;
            return (h & 0x00FFFFFFu) / 8388607.5f - 1f;
        }
    }

    public int SaveAllLoadedChunks()
    {
        if (!Directory.Exists(ChunksDir))
            Directory.CreateDirectory(ChunksDir);

        List<VoxelChunkData> chunksToSave;
        lock (_chunksLock)
            chunksToSave = _chunks.Values.ToList();

        var savedCount = 0;
        foreach (var chunk in chunksToSave)
        {
            try
            {
                var path = Path.Combine(ChunksDir, $"chunk_{chunk.Coord.X}_{chunk.Coord.Y}_{chunk.Coord.Z}.bin");
                chunk.Save(path);
                savedCount++;
            }
            catch (Exception ex)
            {
                _log.Warn($"Failed to save chunk {chunk.Coord}: {ex.Message}");
            }
        }

        return savedCount;
    }

    private static float HashNoise3D(int x, int y, int z, int seed)
    {
        unchecked
        {
            var h = (uint)seed;
            h ^= (uint)x * 0x9E3779B9u;
            h ^= (uint)y * 0xC2B2AE35u;
            h ^= (uint)z * 0x85EBCA6Bu;
            h ^= h >> 16;
            h *= 0x7FEB352Du;
            h ^= h >> 15;
            h *= 0x846CA68Bu;
            h ^= h >> 16;
            return (h & 0x00FFFFFFu) / 8388607.5f - 1f;
        }
    }

    private static float SmoothStep(float t) => t * t * (3f - 2f * t);
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    public void UnloadChunks(HashSet<ChunkCoord> keep, Action<ChunkCoord, byte[]>? onSaveRequest = null)
    {
        List<(ChunkCoord coord, byte[]? blocks)> toSave;
        List<ChunkCoord> toRemove;
        
        lock (_chunksLock)
        {
            // Create snapshot of keys to avoid modification during enumeration
            var keys = _chunks.Keys.ToArray();
            toRemove = new List<ChunkCoord>();
            
            foreach (var coord in keys)
            {
                if (!keep.Contains(coord))
                    toRemove.Add(coord);
            }

            // Collect save data while still under lock
            toSave = new List<(ChunkCoord, byte[]?)>();
            foreach (var coord in toRemove)
            {
                if (_chunks.TryGetValue(coord, out var chunk) && chunk.NeedsSave)
                {
                    // Create a copy of blocks for async save
                    var blocksCopy = new byte[chunk.Blocks.Length];
                    Array.Copy(chunk.Blocks, blocksCopy, blocksCopy.Length);
                    toSave.Add((coord, blocksCopy));
                }
            }

            // Remove chunks from memory while still under lock
            foreach (var coord in toRemove)
            {
                _chunks.Remove(coord);
                ChunkSeamRegistry.Unregister(coord);
            }
        }

        // Call save requests OUTSIDE the lock (prefer pattern A)
        if (onSaveRequest != null)
        {
            foreach (var (coord, blocks) in toSave)
            {
                if (blocks != null)
                    onSaveRequest(coord, blocks);
            }
        }
    }

    public static ChunkCoord WorldToChunk(int wx, int wy, int wz, out int lx, out int ly, out int lz)
    {
        var cx = FloorDiv(wx, VoxelChunkData.ChunkSizeX);
        var cy = FloorDiv(wy, VoxelChunkData.ChunkSizeY);
        var cz = FloorDiv(wz, VoxelChunkData.ChunkSizeZ);

        lx = Mod(wx, VoxelChunkData.ChunkSizeX);
        ly = Mod(wy, VoxelChunkData.ChunkSizeY);
        lz = Mod(wz, VoxelChunkData.ChunkSizeZ);

        return new ChunkCoord(cx, cy, cz);
    }

    private void LoadChunks()
    {
        if (!Directory.Exists(ChunksDir))
        {
            _log.Warn($"Chunks folder missing: {ChunksDir}");
            return;
        }

        var files = Directory.GetFiles(ChunksDir, "chunk_*.bin");
        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            ChunkCoord? coord = null;
            if (fileName != null)
                coord = TryParseCoord(fileName);

            var chunk = new VoxelChunkData(coord ?? new ChunkCoord(0, 0, 0));
            chunk.Load(file);
            if (chunk == null)
                continue;

            if (_chunks.ContainsKey(chunk.Coord))
            {
                _log.Warn($"Duplicate chunk at {chunk.Coord} in {file}");
                continue;
            }

            _chunks[chunk.Coord] = chunk;
            ChunkSeamRegistry.Register(chunk.Coord, ChunkSurfaceProfile.FromChunk(chunk));
        }

        if (_chunks.Count == 0)
            _log.Warn($"No chunks loaded from {ChunksDir}");
    }

    private VoxelChunkData? TryLoadChunk(ChunkCoord coord)
    {
        var path = Path.Combine(ChunksDir, $"chunk_{coord.X}_{coord.Y}_{coord.Z}.bin");
        if (!File.Exists(path))
            return null;
        var chunk = new VoxelChunkData(coord);
        chunk.Load(path);
        return chunk;
    }

    private bool IsChunkYInRange(int cy) => cy >= 0 && cy <= _maxChunkY;

    private static ChunkCoord? TryParseCoord(string fileName)
    {
        var match = ChunkNameRegex.Match(fileName);
        if (!match.Success)
            return null;

        if (!int.TryParse(match.Groups["x"].Value, out var x))
            return null;
        if (!int.TryParse(match.Groups["y"].Value, out var y))
            return null;
        if (!int.TryParse(match.Groups["z"].Value, out var z))
            return null;

        return new ChunkCoord(x, y, z);
    }

    private static int FloorDiv(int value, int divisor)
    {
        var q = value / divisor;
        var r = value % divisor;
        if (r != 0 && ((r > 0) != (divisor > 0)))
            q--;
        return q;
    }

    private static int Mod(int value, int divisor)
    {
        var m = value % divisor;
        if (m < 0)
            m += Math.Abs(divisor);
        return m;
    }
}
