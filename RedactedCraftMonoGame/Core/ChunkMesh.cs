using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RedactedCraftMonoGame.Core;

public sealed class ChunkMesh
{
    public ChunkCoord Coord { get; }
    public VertexPositionTexture[] OpaqueVertices { get; }
    public VertexPositionTexture[] TransparentVertices { get; }
    public BoundingBox Bounds { get; }

    public int OpaqueTriangles => OpaqueVertices.Length / 3;
    public int TransparentTriangles => TransparentVertices.Length / 3;

    public ChunkMesh(ChunkCoord coord, VertexPositionTexture[] opaque, VertexPositionTexture[] transparent, BoundingBox bounds)
    {
        Coord = coord;
        OpaqueVertices = opaque;
        TransparentVertices = transparent;
        Bounds = bounds;
    }
}
