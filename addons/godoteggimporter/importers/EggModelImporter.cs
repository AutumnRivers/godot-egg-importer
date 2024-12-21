using Godot;
using Godot.Collections;

using Panda3DEggParser;

using System;
using System.Collections.Generic;
using System.Linq;

[Tool]
public partial class EggModelImporter : EditorImportPlugin
{
    public override string _GetImporterName()
    {
        return "autumnrivers.panda3d.egg";
    }

    public override string _GetVisibleName()
    {
        return "Panda3D EGG Model";
    }

    public override string[] _GetRecognizedExtensions()
    {
        return ["egg"];
    }

    public override string _GetSaveExtension()
    {
        return "tscn";
    }

    public override string _GetResourceType()
    {
        return "PackedScene";
    }

    public override int _GetPresetCount()
    {
        return 1;
    }

    public override string _GetPresetName(int presetIndex)
    {
        return "Default";
    }

    public override float _GetPriority()
    {
        return 2.0f;
    }

    public override int _GetImportOrder()
    {
        return (int)ImportOrder.Default;
    }

    public override bool _GetOptionVisibility(string path, StringName optionName, Dictionary options)
    {
        return true;
    }

    public override Array<Dictionary> _GetImportOptions(string path, int presetIndex)
    {
        return new Array<Dictionary>()
        {
            new Dictionary()
            {
                { "name", "force_animation" },
                { "default_value", false }
            },
            new Dictionary()
            {
                { "name", "automatically_convert_collisions" },
                { "default_value", true }
            }
        };
    }

    public override Error _Import(string sourceFile, string savePath, Dictionary options, Array<string> platformVariants, Array<string> genFiles)
    {
        EggParser parser;

        using var file = FileAccess.Open(sourceFile, FileAccess.ModeFlags.Read);
        if(file.GetError() != Error.Ok)
        {
            GD.Print(file.GetError());
            return Error.Failed;
        }

        PackedScene scene = new PackedScene();
        Node3D root = createRoot();
        string rootName = savePath.Split(".egg")[0].Split('/').Last();
        root.Name = rootName;
        parser = new EggParser(file.GetAsText());
        Panda3DEgg eggData = parser.Parse();

        foreach(var eggGroup in eggData.Data)
        {
            ParseGroupData(eggGroup);
        }

        void ParseGroupData(EggGroup group)
        {
            if (group is not EntityGroup) return;
            EntityGroup entityGroup = (EntityGroup)group;
            bool isPolygonGroup = entityGroup.Members.Any(m => m is Polygon);
            if(isPolygonGroup)
            {
                MeshInstance3D mesh = ParsePolygonGroup(entityGroup, eggData);
                root.AddChild(mesh);
                mesh.Owner = root;
            } else if(entityGroup.Members.Any(m => m is EntityGroup))
            {
                foreach(var subgroup in entityGroup.Members.Where(m => m is EntityGroup))
                {
                    ParseGroupData(subgroup);
                }
            }
        }

        scene.Pack(root);

        string filename = $"{savePath}.{_GetSaveExtension()}";
        GD.Print(filename);
        var saver = ResourceSaver.Save(scene, filename);
        GD.Print(saver);
        return saver;
    }

    private Node3D createRoot()
    {
        return new Node3D();
    }

    private MeshInstance3D ParsePolygonGroup(EntityGroup group, Panda3DEgg egg)
    {
        MeshInstance3D meshInstance = new MeshInstance3D();
        ArrayMesh polygonMesh = new ArrayMesh();
        StandardMaterial3D polygonMat = new StandardMaterial3D();

        // TODO: Seperate surface for each texture
        Godot.Collections.Array surfaceArray = new();
        surfaceArray.Resize((int)Mesh.ArrayType.Max);
        List<Vector3> allVerticies = new();
        List<Vector2> allUVs = new();
        foreach(Polygon polygon in group.Members.Where(m => m is Polygon))
        {
            Vector3[] verticies = new Vector3[polygon.VertexRef.Indices.Length];
            int vertIndex = 0;
            foreach (var vert in polygon.VertexRef.Indices)
            {
                Vector3 vertex3;
                Vertex referencedVertex = GetVertexFromPool(egg, vert, polygon.VertexRef.Pool);
                vertex3 = new Vector3(referencedVertex.X, referencedVertex.Y, referencedVertex.Z);
                verticies[vertIndex] = vertex3;
                vertIndex++;
                allVerticies.Add(vertex3);
                if(referencedVertex.UV != default)
                {
                    allUVs.Add(new Vector2(referencedVertex.UV.U, referencedVertex.UV.V));
                }
            }
        }
        surfaceArray[(int)Mesh.ArrayType.Vertex] = allVerticies.ToArray();
        if(allUVs.Any())
        {
            surfaceArray[(int)Mesh.ArrayType.TexUV] = allUVs.ToArray();
        }

        // quickfix
        polygonMat.CullMode = BaseMaterial3D.CullModeEnum.Front;

        polygonMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surfaceArray);
        polygonMesh.SurfaceSetMaterial(0, polygonMat);
        meshInstance.Mesh = polygonMesh;
        meshInstance.Name = group.Name;
        meshInstance.RotateX(Mathf.DegToRad(-90));
        return meshInstance;
    }

    private CollisionShape3D ParseCollisionGroup(EntityGroup group)
    {
        CollisionShape3D collisionShape = new CollisionShape3D();
        ArrayMesh polygonMesh = new ArrayMesh();

        // TODO: Colliders have their own VertexPool???
        return collisionShape;
    }

    private Vertex GetVertexFromPool(Panda3DEgg egg, int index, string poolName)
    {
        var pool = egg.Data.FirstOrDefault(g => (g is VertexPool) && g.Name == poolName);

        if(pool == default)
        {
            throw new Exception("Invalid vertex pool " + poolName);
        }

        var poolRef = (VertexPool)pool;
        return poolRef.References.FirstOrDefault(v => v.Index == index);
    }
}