using System.Numerics;

namespace SoftwareRenderer.ModelParser;

public interface IVertex
{
    Vector3 Position { get; }
    Vector3 Normal { get; }
    Vector2 UV { get; }
}

public interface IMaterial
{
    string Name { get; }
}

public interface ICluster
{
    int MaterialIndex { get; }
    int StartIndex { get; }
    int IndexCount { get; }
}

public interface IOBJHandle
{
    IVertex[] Vertices { get; }
    int[] Indices { get; }
    ICluster[] Clusters { get; }
    IMaterial[] Materials { get; }
}
