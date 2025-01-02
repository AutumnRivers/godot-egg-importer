using Godot;
using Godot.Collections;

using Panda3DEggParser;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

[Tool]
public partial class EggModelImporter : EditorImportPlugin
{
    public override string _GetImporterName()
    {
        return "autumnrivers.panda3d.egg_model";
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
        return 3.0f;
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
            },
            new Dictionary()
            {
                { "name", "force_import_possible_animations" },
                { "default_value", false }
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

        if(eggData.Data.Any(g => g is Table))
        {
            if ((bool)options["force_import_possible_animations"])
            {
                GD.PushWarning("EGG MODEL IMPORT WARNING: " +
                    "You're attempting to import a possible animation. " +
                    "Errors will very likely occur! " +
                    "Do not report any errors you get! You knew what you were getting into.");
            } else
            {
                GD.PrintErr("EGG MODEL IMPORT FAILED: " +
                    $"The egg file at {sourceFile} is very likely an animation. " +
                    "You are attempting to import it as a model. This will fail.\n" +
                    "Go to the file, and reimport it as a 'Panda3D EGG Animation'.\n" +
                    "If you want to import it as a model anyway, enable the " +
                    "'Force Import Possible Animations' option. " +
                    "You will NOT get any support if you enable this, though!");
                return Error.Failed;
            }
        }

        foreach(var eggGroup in eggData.Data)
        {
            ParseGroupData(eggGroup);
        }

        void ParseGroupData(EggGroup group)
        {
            if (group is not EntityGroup) return;
            EntityGroup entityGroup = (EntityGroup)group;
            bool isPolygonGroup = entityGroup.Members.Any(m => m is Polygon);
            if (isPolygonGroup)
            {
                if(entityGroup.IsCollision)
                {
                    if(!(bool)options["automatically_convert_collisions"]) return;
                    var existingBody = root.GetNodeOrNull("StaticBody3D");
                    if(existingBody == null)
                    {
                        StaticBody3D body = new();
                        body.Name = "StaticBody3D";
                        root.AddChild(body);
                        body.Owner = root;
                        existingBody = body;
                    }
                    var colShape = ParseCollisionGroup(entityGroup, eggData, options);
                    colShape.Name = "Collision";
                    existingBody.AddChild(colShape);
                    colShape.Owner = root;
                } else
                {
                    MeshInstance3D mesh = ParsePolygonGroup(entityGroup, eggData, options);
                    root.AddChild(mesh);
                    mesh.Owner = root;
                }
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
        var saver = ResourceSaver.Save(scene, filename);
        return saver;
    }

    private Node3D createRoot()
    {
        return new Node3D();
    }

    // TODO: Rewrite this with SurfaceTool
    private MeshInstance3D ParsePolygonGroup(EntityGroup group, Panda3DEgg egg, Dictionary options = null, Skeleton3D bodySkele = null)
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
                { "colors", new Array<Color>() },
                { "normals", new Array<Vector3>() }
            }
        };

        string surfaceName = "default_surface";
        foreach (Polygon polygon in group.Members.Where(m => m is Polygon))
        {
            if (polygon.TRef != default) surfaceName = polygon.TRef;
            if (!CheckIfSurfaceExists(surfaceName)) AddNewSurface(surfaceName);
            if(polygon.TRef != default)
            {
                TextureGroup polygonTex = (TextureGroup)egg.Data.FirstOrDefault(g => g is TextureGroup && g.Name == polygon.TRef);
                string filepath = polygonTex.Filepath;
                if ((string)options["change_maps_directory"] != string.Empty)
                {
                    string filename = filepath.Split("maps/")[1];
                    filepath = (string)options["change_maps_directory"] + '/' + filename;
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
            foreach (var index in polygon.VertexRef.Indices)
            {
                Vector3 vertex3;
                Vertex referencedVertex = GetVertexFromPool(egg, index, polygon.VertexRef.Pool);
                // We swap Y and Z because Panda3D is a Z-Up game engine
                if((bool)options["convert_model_coordinates"]) {
                    vertex3 = new Vector3(referencedVertex.X, referencedVertex.Z, referencedVertex.Y);
                } else {
                    vertex3 = new Vector3(referencedVertex.X, referencedVertex.Y, referencedVertex.Z);
                }
                verticies[vertIndex] = vertex3;
                vertIndex++;
                if(referencedVertex.UV != default)
                {
                    var uv = new Vector2((float)referencedVertex.UV.U, 1 - (float)referencedVertex.UV.V);
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
                if(referencedVertex.Normal != default)
                {
                    AddNormalToSurface(surfaceName, new Vector3(
                        (float)referencedVertex.Normal.X,
                        (float)referencedVertex.Normal.Z,
                        (float)referencedVertex.Normal.Y));
                } else
                {
                    AddNormalToSurface(surfaceName, new Vector3(0.5f, 1.0f, 0.5f));
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
            if(((Array<Vector3>)surface["vertices"]).Count % 3 != 0)
            {
                while(((Array<Vector3>)surface["vertices"]).Count % 3 != 0)
                {
                    ((Array<Vector3>)surface["vertices"]).Add(new Vector3(0,0,0));
                }
            }
            surfaceArray[(int)Mesh.ArrayType.Vertex] = ((Array<Vector3>)surface["vertices"]).ToArray();
            surfaceArray[(int)Mesh.ArrayType.TexUV] = ((Array<Vector2>)surface["uvs"]).ToArray();
            surfaceArray[(int)Mesh.ArrayType.Color] = ((Array<Color>)surface["colors"]).ToArray();
            surfaceArray[(int)Mesh.ArrayType.Normal] = ((Array<Vector3>)surface["normals"]).ToArray();
            polygonMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surfaceArray, flags: Mesh.ArrayFormat.FlagUse8BoneWeights);
        }
        for (int i = 0; i < polygonMesh.GetSurfaceCount(); i++)
        {
            polygonMesh.SurfaceSetMaterial(i, polygonMats[i]);
        }

        meshInstance.Mesh = polygonMesh;
        meshInstance.Name = group.Name;
        if(group.Members.Any(m => m is Transform))
        {
            var transform = (Transform)group.Members.First(m => m is Transform);
            if(transform.Translate.Any(m => m != 0.0))
            {
                meshInstance.Position = new Vector3((float)transform.Translate[0],
                    (float)transform.Translate[2], (float)-transform.Translate[1]);
            }
        }
        // STUPID FIX ALERT // STUPID FIX ALERT //
        if((bool)options["convert_model_coordinates"]) meshInstance.Scale = new Vector3(1, 1, -1);
        // END STUPID FIX // END STUPID FIX //
        return meshInstance;

        void AddNewSurface(string surfaceName)
        {
            if (surfaces.Any(s => (string)s["name"] == surfaceName)) return;
            int mindex = surfaces.Count - 1;
            if (!surfaces.Any(s => (string)s["name"] != "default_surface"))
            {
                mindex = 0;
            }
            StandardMaterial3D mat = new();
            if ((bool)options["set_materials_to_unshaded"])
            {
                mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            }
            mat.ResourceName = surfaceName;
            polygonMats.Add(mat);
            surfaces.Add(new()
            {
                { "name", surfaceName },
                { "material_index", mindex },
                { "texture_filepath", string.Empty },
                { "uvs", new Array<Vector2>() },
                { "vertices", new Array<Vector3>() },
                { "colors", new Array<Color>() },
                { "normals", new Array<Vector3>() }
            });
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

        void AddNormalToSurface(string surfaceName, Vector3 normal)
        {
            var surface = surfaces.FirstOrDefault(s => (string)s["name"] == surfaceName);
            if (surface == default) return;
            int index = surfaces.IndexOf(surface);
            ((Array<Vector3>)surface["normals"]).Add(normal);
        }
    }

    private MeshInstance3D ParsePolygonGroupST(EntityGroup group, Panda3DEgg egg, Dictionary options = null, Skeleton3D bodySkele = null,
        Skin bodySkin = null)
    {
        SurfaceTool surfaceTool = new();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);
        string current_tref = string.Empty;
        StandardMaterial3D defaultMaterial = new StandardMaterial3D();
        if ((bool)options["set_materials_to_unshaded"])
        {
            defaultMaterial.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        }
        System.Collections.Generic.Dictionary<string, StandardMaterial3D> materials = new()
        {
            { "default", defaultMaterial }
        };
        string currentMaterial = "default";
        foreach (Polygon polygon in group.Members.Where(g => g is Polygon))
        {
            string pool = polygon.VertexRef.Pool;
            string tref = polygon.TRef;
            if(tref != default && tref != string.Empty && tref != currentMaterial)
            {
                currentMaterial = tref;

                if (!materials.ContainsKey(currentMaterial))
                {
                    StandardMaterial3D tref_material = new StandardMaterial3D();
                    if ((bool)options["set_materials_to_unshaded"])
                    {
                        tref_material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
                    }
                    materials.Add(tref, tref_material);
                }

                TextureGroup polygonTex = polygon.FindTextureInEgg(egg);
                string filepath = polygonTex.Filepath;
                if ((string)options["change_maps_directory"] != string.Empty)
                {
                    string filename = filepath.Split("maps/")[1];
                    filepath = (string)options["change_maps_directory"] + '/' + filename;
                }

                if (FileAccess.FileExists(filepath))
                {
                    Texture2D tex = ResourceLoader.Load<Texture2D>(filepath);
                    materials[currentMaterial].AlbedoTexture = tex;
                }

                surfaceTool.Commit();
                surfaceTool.SetMaterial(materials[currentMaterial]);
            }

            foreach(var index in polygon.VertexRef.Indices)
            {
                Vertex vertex = egg.VertexPools.First(p => p.Name == pool).References.First(r => r.Index == index);

                surfaceTool.SetNormal(new Vector3(0.5f, 0.5f, 1.0f));
                if(vertex.UV != null)
                {
                    surfaceTool.SetUV(new Vector2((float)vertex.UV.U, (float)vertex.UV.V));
                }
                if(vertex.RGBA != null)
                {
                    surfaceTool.SetColor(new Color((float)vertex.RGBA.R,
                        (float)vertex.RGBA.G,
                        (float)vertex.RGBA.B,
                        (float)vertex.RGBA.A));
                } else { surfaceTool.SetColor(new Color(255, 255, 255)); }

                var joints = egg.Joints.Where(j => j.VertexRef.Any(vr => vr.Indices.Contains(index)));
                if(joints.Any())
                {
                    int[] jointIndices = [0, 0, 0, 0, 0, 0, 0, 0];
                    float[] jointWeights = [0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f];
                    bool tooManyJoints = joints.Count() > 8;
                    List<Joint> jointsList = joints.ToList();

                    if(tooManyJoints)
                    {
                        jointsList.RemoveRange(8, jointsList.Count - 8);
                    }

                    for(int i = 0; i < jointsList.Count; i++)
                    {
                        var joint = jointsList[i];
                        //jointIndices[i] = egg.Joints.IndexOf(joint);
                        jointIndices[i] = bodySkele.FindBone(joint.Name);
                        //jointIndices[i] = bodySkin.bin
                        //GD.Print((float)polygon.VertexRef.Membership);
                        jointWeights[i] = (float)joint.VertexRef.First(vr => vr.Indices.Contains(index)).Membership;
                        //GD.Print($"{jointIndices[i]} : {jointWeights[i]}");
                        GD.Print($"{bodySkele.FindBone(joint.Name)} : {joint.Name} : {jointWeights[i]}");
                    }

                    surfaceTool.SetBones(jointIndices);
                    surfaceTool.SetWeights(jointWeights);
                }

                // We swap Y and Z because Godot is a Y-Up engine, while Panda3D is a Z-Up engine.
                surfaceTool.AddVertex(new Vector3(vertex.X, vertex.Z, vertex.Y));
            }
        }

        MeshInstance3D meshInstance = new();
        meshInstance.Mesh = surfaceTool.Commit();
        meshInstance.Name = group.Name;

        return meshInstance;
    }

    private CollisionShape3D ParseCollisionGroup(EntityGroup group, Panda3DEgg egg, Dictionary options)
    {
        CollisionShape3D collisionShape = new CollisionShape3D();

        SurfaceTool surfaceTool = new();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);
        foreach(Polygon polygon in group.Members.Where(m => m is Polygon))
        {
            foreach(var index in polygon.VertexRef.Indices)
            {
                Vertex vertex = egg.VertexPools.First(p => p.Name == polygon.VertexRef.Pool).References.First(r => r.Index == index);
                if ((bool)options["convert_model_coordinates"])
                {
                    surfaceTool.AddVertex(new Vector3(vertex.X, vertex.Z, vertex.Y));
                } else
                {
                    surfaceTool.AddVertex(new Vector3(vertex.X, vertex.Y, vertex.Z));
                }   
            }
        }

        collisionShape.Shape = surfaceTool.Commit().CreateConvexShape();
        return collisionShape;
    }

    private Vertex GetVertexFromPool(Panda3DEgg egg, int index, string poolName)
    {
        var pool = egg.VertexPools.FirstOrDefault(p => p.Name == poolName);

        if(pool == default)
        {
            throw new Exception("Invalid vertex pool " + poolName);
        }

        var poolRef = pool;
        return poolRef.References.FirstOrDefault(v => v.Index == index);
    }
}