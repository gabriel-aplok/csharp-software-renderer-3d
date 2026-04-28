using System.Numerics;

namespace SoftwareRenderer.ModelParser;

internal class Vertex : IVertex
{
    public Vector3 Position { get; set; }
    public Vector3 Normal { get; set; }
    public Vector2 UV { get; set; }
}

internal record Material(int Index, string Name) : IMaterial;

internal class Cluster(int materialIndex, int startIndex) : ICluster
{
    public int MaterialIndex { get; } = materialIndex;
    public int StartIndex { get; } = startIndex;
    public int IndexCount { get; set; } = 0;
}

internal class OBJHandle : IOBJHandle
{
    public IVertex[] Vertices { get; set; } = [];
    public int[] Indices { get; set; } = [];
    public ICluster[] Clusters { get; set; } = [];
    public IMaterial[] Materials { get; set; } = [];
}
