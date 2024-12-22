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
            },
            new Dictionary()
            {
                { "name", "set_materials_to_unshaded" },
                { "default_value", false }
            },
            new Dictionary()
            {
                { "name", "convert_model_coordinates" },
                { "default_value", true }
            },
            new Dictionary()
            {
                { "name", "change_maps_directory" },
                { "default_value", "res://maps" },
                { "property_hint", (int)PropertyHint.Dir }
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
                MeshInstance3D mesh = ParsePolygonGroup(entityGroup, eggData, options);
                root.AddChild(mesh);
                mesh.Owner = root;
            }
            if(entityGroup.Members.Any(m => m is EntityGroup))
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

    private MeshInstance3D ParsePolygonGroup(EntityGroup group, Panda3DEgg egg, Dictionary options = null)
    {
        MeshInstance3D meshInstance = new MeshInstance3D();
        ArrayMesh polygonMesh = new ArrayMesh();
        StandardMaterial3D polygonMat = new StandardMaterial3D();
        if((bool)options["set_materials_to_unshaded"])
        {
            polygonMat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        }

        List<StandardMaterial3D> polygonMats = new()
        {
            polygonMat
        };
        Array<Dictionary> surfaces = new()
        {
            new()
            {
                { "name", "default_surface" },
                { "material_index", 0 },
                { "texture_filepath", string.Empty },
                { "uvs", new Array<Vector2>() },
                { "vertices", new Array<Vector3>() },
                { "colors", new Array<Color>() }
            }
        };

        foreach(Polygon polygon in group.Members.Where(m => m is Polygon))
        {
            string surfaceName = "default_surface";
            if (polygon.TRef != default) surfaceName = polygon.TRef;
            if (!CheckIfSurfaceExists(surfaceName)) AddNewSurface(surfaceName);
            if(polygon.TRef != default)
            {
                TextureGroup polygonTex = polygon.FindTextureInEgg(egg);
                string filepath = polygonTex.Filepath;
                if ((string)options["change_maps_directory"] != string.Empty)
                {
                    string filename = filepath.Split("maps/")[1];
                    filepath = (string)options["change_maps_directory"] + '/' + filename;
                    GD.Print(filepath);
                }
                AddTextureFilepathToSurface(surfaceName, filepath);
                if(FileAccess.FileExists(filepath))
                {
                    Texture2D tex = ResourceLoader.Load<Texture2D>(filepath);
                    AddTextureToMaterial(GetSurfaceMaterialIndex(surfaceName), tex);
                }
            }
            Vector3[] verticies = new Vector3[polygon.VertexRef.Indices.Length];
            int vertIndex = 0;
            foreach (var vert in polygon.VertexRef.Indices)
            {
                Vector3 vertex3;
                Vertex referencedVertex = GetVertexFromPool(egg, vert, polygon.VertexRef.Pool);
                // We swap X and Z because Panda3D is a Z-Up game engine
                vertex3 = new Vector3(referencedVertex.X, referencedVertex.Z, referencedVertex.Y);
                verticies[vertIndex] = vertex3;
                vertIndex++;
                if(referencedVertex.UV != default)
                {
                    var uv = new Vector2((float)referencedVertex.UV.U, (float)-referencedVertex.UV.V);
                    AddUVToSurface(surfaceName, uv);
                } else
                {
                    AddUVToSurface(surfaceName, Vector2.Zero);
                }
                if(referencedVertex.RGBA != default)
                {
                    Color color = new Color((float)referencedVertex.RGBA.R,
                        (float)referencedVertex.RGBA.G,
                        (float)referencedVertex.RGBA.B,
                        (float)referencedVertex.RGBA.A);
                    AddColorToSurface(surfaceName, color);
                } else
                {
                    AddColorToSurface(surfaceName, new Color(1, 1, 1, 1));
                }
            }
            AddVerticesToSurface(surfaceName, verticies);
        }
        foreach(var surface in surfaces)
        {
            // STUPID FIX ALERT // STUPID FIX ALERT //
            if (((Array<Vector3>)surface["vertices"]).Count <= 0) continue;
            // END STUPID FIX // END STUPID FIX //

            Godot.Collections.Array surfaceArray = new();
            surfaceArray.Resize((int)Mesh.ArrayType.Max);
            surfaceArray[(int)Mesh.ArrayType.Vertex] = ((Array<Vector3>)surface["vertices"]).ToArray();
            surfaceArray[(int)Mesh.ArrayType.TexUV] = ((Array<Vector2>)surface["uvs"]).ToArray();
            surfaceArray[(int)Mesh.ArrayType.Color] = ((Array<Color>)surface["colors"]).ToArray();
            polygonMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surfaceArray);
        }
        for (int i = 0; i < polygonMesh.GetSurfaceCount(); i++)
        {
            polygonMesh.SurfaceSetMaterial(i, polygonMats[i]);
        }

        meshInstance.Mesh = polygonMesh;
        meshInstance.Name = group.Name;
        return meshInstance;

        void AddNewSurface(string surfaceName)
        {
            if (surfaces.Any(s => (string)s["name"] == surfaceName)) return;
            surfaces.Add(new()
            {
                { "name", surfaceName },
                { "material_index", surfaces.Count - 1 },
                { "texture_filepath", string.Empty },
                { "uvs", new Array<Vector2>() },
                { "vertices", new Array<Vector3>() },
                { "colors", new Array<Color>() }
            });
            StandardMaterial3D mat = new();
            if ((bool)options["set_materials_to_unshaded"])
            {
                mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            }
            polygonMats.Add(mat);
        }

        void AddVerticesToSurface(string surfaceName, Vector3[] vertices)
        {
            if (vertices.Length <= 0) return;
            var surface = surfaces.FirstOrDefault(s => (string)s["name"] == surfaceName);
            if (surface == default) return;
            int index = surfaces.IndexOf(surface);
            var verts = ((Array<Vector3>)surfaces[index]["vertices"]);
            verts.AddRange(vertices);
            surfaces[index]["vertices"] = verts;
        }

        void AddUVsToSurface(string surfaceName, Array<Vector2> uvs)
        {
            var surface = surfaces.FirstOrDefault(s => (string)s["name"] == surfaceName);
            if (surface == default) return;
            int index = surfaces.IndexOf(surface);
            //surfaces[index]["uvs"] = uvs;
            ((Array<Vector2>)surfaces[index]["uvs"]).AddRange(uvs);
        }

        void AddUVToSurface(string surfaceName, Vector2 uv)
        {
            var surface = surfaces.FirstOrDefault(s => (string)s["name"] == surfaceName);
            if (surface == default) return;
            int index = surfaces.IndexOf(surface);
            //surfaces[index]["uvs"] = uvs;
            ((Array<Vector2>)surfaces[index]["uvs"]).Add(uv);
        }

        void AddColorToSurface(string surfaceName, Color color)
        {
            var surface = surfaces.FirstOrDefault(s => (string)s["name"] == surfaceName);
            if (surface == default) return;
            int index = surfaces.IndexOf(surface);
            //surfaces[index]["colors"] = colors;
            ((Array<Color>)surfaces[index]["colors"]).Add(color);
        }

        void AddColorsToSurface(string surfaceName, Array<Color> colors)
        {
            var surface = surfaces.FirstOrDefault(s => (string)s["name"] == surfaceName);
            if (surface == default) return;
            int index = surfaces.IndexOf(surface);
            //surfaces[index]["colors"] = colors;
            ((Array<Color>)surfaces[index]["colors"]).AddRange(colors);
        }

        bool CheckIfSurfaceExists(string surfaceName)
        {
            var surface = surfaces.FirstOrDefault(s => (string)s["name"] == surfaceName);
            return surface != default;
        }

        void AddTextureFilepathToSurface(string surfaceName, string filepath)
        {
            var surface = surfaces.FirstOrDefault(s => (string)s["name"] == surfaceName);
            if (surface == default) return;
            int index = surfaces.IndexOf(surface);
            surface["texture_filepath"] = filepath;
        }

        int GetSurfaceMaterialIndex(string surfaceName)
        {
            var surface = surfaces.FirstOrDefault(s => (string)s["name"] == surfaceName);
            if (surface == default) return -1;
            int index = surfaces.IndexOf(surface);
            return (int)surface["material_index"];
        }

        void AddTextureToMaterial(int idx, Texture2D tex)
        {
            polygonMats[idx].AlbedoTexture = tex;
        }
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