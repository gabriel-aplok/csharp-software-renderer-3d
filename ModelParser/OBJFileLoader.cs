using System.Globalization;
using System.Numerics;

namespace SoftwareRenderer.ModelParser;

public static class OBJFileLoader
{
    private readonly record struct VertexKey(int Position, int Normal, int UV);

    private struct VertexIndex
    {
        public int Position;
        public int Normal;
        public int UV;

        public VertexIndex()
        {
            Position = -1;
            Normal = -1;
            UV = -1;
        }
    }

    public static IOBJHandle CreateHandle() => new OBJHandle();

    public static bool Load(IOBJHandle handle, string path)
    {
        if (!File.Exists(path) || handle is not OBJHandle objHandle)
        {
            Console.WriteLine($"Invalid path or handle. Path = {path}");
            return false;
        }

        var positionList = new List<Vector3>();
        var normalList = new List<Vector3>();
        var uvList = new List<Vector2>();

        var vertexIndexList = new List<VertexIndex>();
        var clusterList = new List<Cluster>();
        var materialList = new List<Material>();

        foreach (var lineString in File.ReadLines(path))
        {
            var line = lineString.AsSpan().Trim();
            if (line.IsEmpty || line[0] == '#')
                continue;

            int firstSpace = line.IndexOf(' ');
            if (firstSpace == -1)
                continue;

            var prefix = line[..firstSpace];
            var data = line[(firstSpace + 1)..].TrimStart();

            if (prefix.SequenceEqual("v"))
            {
                positionList.Add(ParseVector3(data));
            }
            else if (prefix.SequenceEqual("vn"))
            {
                normalList.Add(ParseVector3(data));
            }
            else if (prefix.SequenceEqual("vt"))
            {
                uvList.Add(ParseVector2(data));
            }
            else if (prefix.SequenceEqual("f"))
            {
                ParseFace(data, vertexIndexList, clusterList);
            }
            else if (prefix.SequenceEqual("usemtl"))
            {
                string materialName = data.ToString();
                var material = materialList.FirstOrDefault(m => m.Name == materialName);
                if (material == null)
                {
                    material = new Material(materialList.Count, materialName);
                    materialList.Add(material);
                }
                clusterList.Add(new Cluster(material.Index, vertexIndexList.Count));
            }
        }

        if (clusterList.Count == 0)
        {
            clusterList.Add(new Cluster(0, 0) { IndexCount = vertexIndexList.Count });
        }
        else
        {
            clusterList[^1].IndexCount = vertexIndexList.Count - clusterList[^1].StartIndex;
        }

        ConstructGeometry(
            objHandle,
            vertexIndexList,
            positionList,
            normalList,
            uvList,
            clusterList,
            materialList
        );
        return true;
    }

    private static void ConstructGeometry(
        OBJHandle objHandle,
        List<VertexIndex> vertexIndexList,
        List<Vector3> positions,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<Cluster> clusters,
        List<Material> materials
    )
    {
        var vertices = new List<Vertex>();
        var indices = new List<int>(vertexIndexList.Count);
        var vertexCache = new Dictionary<VertexKey, int>();

        foreach (var vIndex in vertexIndexList)
        {
            int posIdx = GetRealIndex(vIndex.Position, positions.Count);
            int normIdx = GetRealIndex(vIndex.Normal, normals.Count);
            int uvIdx = GetRealIndex(vIndex.UV, uvs.Count);

            var key = new VertexKey(posIdx, normIdx, uvIdx);

            if (vertexCache.TryGetValue(key, out int cacheIndex))
            {
                indices.Add(cacheIndex);
            }
            else
            {
                var vertex = new Vertex
                {
                    Position = posIdx >= 0 ? positions[posIdx] : default,
                    Normal = normIdx >= 0 ? normals[normIdx] : default,
                    UV = uvIdx >= 0 ? uvs[uvIdx] : new Vector2(-1, -1),
                };

                vertices.Add(vertex);
                indices.Add(vertices.Count - 1);
                vertexCache[key] = vertices.Count - 1;
            }
        }

        objHandle.Vertices = [.. vertices];
        objHandle.Indices = [.. indices];
        objHandle.Clusters = [.. clusters];
        objHandle.Materials = [.. materials];
    }

    private static void ParseFace(
        ReadOnlySpan<char> data,
        List<VertexIndex> vertexIndexList,
        List<Cluster> clusterList
    )
    {
        var vIndexList = new List<VertexIndex>();

        while (!data.IsEmpty)
        {
            int nextSpace = data.IndexOf(' ');
            var token = nextSpace == -1 ? data : data[..nextSpace];

            if (!token.IsEmpty)
            {
                var vIndex = new VertexIndex();
                int slash1 = token.IndexOf('/');

                if (slash1 == -1)
                {
                    vIndex.Position = int.Parse(token) - 1;
                }
                else
                {
                    vIndex.Position = int.Parse(token[..slash1]) - 1;
                    var remainder = token[(slash1 + 1)..];
                    int slash2 = remainder.IndexOf('/');

                    if (slash2 == -1) // v/vt
                    {
                        vIndex.UV = int.Parse(remainder) - 1;
                    }
                    else if (slash2 == 0) // v//vn
                    {
                        vIndex.Normal = int.Parse(remainder[1..]) - 1;
                    }
                    else // v/vt/vn
                    {
                        vIndex.UV = int.Parse(remainder[..slash2]) - 1;
                        vIndex.Normal = int.Parse(remainder[(slash2 + 1)..]) - 1;
                    }
                }
                vIndexList.Add(vIndex);
            }

            if (nextSpace == -1)
                break;
            data = data[(nextSpace + 1)..].TrimStart();
        }

        // triangulate n-gons
        for (int i = 1; i < vIndexList.Count - 1; i++)
        {
            vertexIndexList.Add(vIndexList[0]);
            vertexIndexList.Add(vIndexList[i]);
            vertexIndexList.Add(vIndexList[i + 1]);

            if (clusterList.Count > 0)
                clusterList[^1].IndexCount += 3;
        }
    }

    private static Vector3 ParseVector3(ReadOnlySpan<char> span)
    {
        Span<float> v = stackalloc float[3];
        ParseFloats(span, v);
        return new Vector3(v[0], v[1], v[2]);
    }

    private static Vector2 ParseVector2(ReadOnlySpan<char> span)
    {
        Span<float> v = stackalloc float[2];
        ParseFloats(span, v);
        return new Vector2(v[0], v[1]);
    }

    private static void ParseFloats(ReadOnlySpan<char> span, Span<float> results)
    {
        int index = 0;
        while (!span.IsEmpty && index < results.Length)
        {
            int nextSpace = span.IndexOf(' ');
            var token = nextSpace == -1 ? span : span[..nextSpace];

            if (!token.IsEmpty)
            {
                float.TryParse(
                    token,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out results[index]
                );
                index++;
            }

            if (nextSpace == -1)
                break;
            span = span[(nextSpace + 1)..].TrimStart();
        }
    }

    private static int GetRealIndex(int index, int maxCount) =>
        index < 0 ? maxCount + index : index;
}
