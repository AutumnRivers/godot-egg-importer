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
            if(entityGroup.Members.Any(m => m is Joint))
            {
                var joints = entityGroup.Members.Where(m => m is Joint);
                //var joints = entityGroup.Members.OfType<Joint>().ToList();
                var skeleton = root.GetNodeOrNull("Skeleton3D");
                if(skeleton == null)
                {
                    Skeleton3D skel = new Skeleton3D();
                    skel.Name = "Skeleton3D";
                    skeleton = skel;
                }
                var skeleton3d = (Skeleton3D)skeleton;

                var skin = new Skin();
                foreach (var joint in joints)
                {
                    var pJoint = (Joint)joint;
                    AddBonesToSkeleton(pJoint, ref skeleton3d, ref skin);
                    //skin = skeleton3d.CreateSkinFromRestTransforms();
                    skeleton3d.RegisterSkin(skin);
                }

                root.AddChild(skeleton3d);
                skeleton3d.Owner = root;

                var mesh = root.GetNodeOrNull<MeshInstance3D>(group.Name);
                if(mesh != null)
                {
                    mesh.Skin = skin;
                    mesh.Reparent(skeleton3d);
                    mesh.Skeleton = mesh.GetPathTo(skeleton3d);
                    mesh.Owner = root;
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

    // TODO: Rewrite this with SurfaceTool
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
                { "colors", new Array<Color>() },
                { "bones", new Array<int>() },
                { "bone_weights", new Array<float>() },
                { "indices", new Array<int>() }
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
                AddIndexToSurface(surfaceName, vert);
                bool hasAssociatedJoints = egg.Joints.Any(j => j.VertexRef.Any(v => v.Indices.Contains(vert)));
                if(hasAssociatedJoints)
                {
                    var associatedJoints = egg.Joints.Where(j => j.VertexRef.Any(v => v.Indices.Contains(vert)));
                    foreach (var associatedJoint in associatedJoints)
                    {
                        AddBoneToSurface(surfaceName, egg.Joints.IndexOf(associatedJoint));
                    }
                }
                Vector3 vertex3;
                Vertex referencedVertex = GetVertexFromPool(egg, vert, polygon.VertexRef.Pool);
                // We swap Y and Z because Panda3D is a Z-Up game engine
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
            //surfaceArray[(int)Mesh.ArrayType.Index] = ((Array<int>)surface["indices"]).ToArray();
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
                { "colors", new Array<Color>() },
                { "bones", new Array<int>() },
                { "bone_weights", new Array<float>() },
                { "indices", new Array<int>() }
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

        void AddIndexToSurface(string surfaceName, int index)
        {
            var surface = surfaces.FirstOrDefault(s => (string)s["name"] == surfaceName);
            if (surface == default) return;
            int surfaceIndex = surfaces.IndexOf(surface);
            ((Array<int>)surfaces[surfaceIndex]["indices"]).Add(index);
        }

        void AddBoneToSurface(string surfaceName, int boneIndex)
        {
            var surface = surfaces.FirstOrDefault(s => (string)s["name"] == surfaceName);
            if (surface == default) return;
            int surfaceIndex = surfaces.IndexOf(surface);
            ((Array<int>)surfaces[surfaceIndex]["bones"]).Add(boneIndex);
        }
    }

    private CollisionShape3D ParseCollisionGroup(EntityGroup group)
    {
        CollisionShape3D collisionShape = new CollisionShape3D();
        ArrayMesh polygonMesh = new ArrayMesh();

        // TODO: Colliders have their own VertexPool???
        return collisionShape;
    }

    private void AddBonesToSkeleton(Joint jointGroup, ref Skeleton3D skeleton, ref Skin skin, int parentIndex = -1)
    {
        int boneIndex = skeleton.AddBone(jointGroup.Name);
        Transform3D currentTransform;
        if(jointGroup.Transform == null)
        {
            currentTransform = Transform3D.Identity;
            if(boneIndex == 0) currentTransform = currentTransform.Rotated(new Vector3(1, 0, 0), Mathf.DegToRad(-90));
        } else
        {
            currentTransform = GetTransformFromMatrix4(jointGroup.Transform.Matrix4);
        }
        Transform3D defaultTransform;
        if(jointGroup.DefaultPose == null)
        {
            defaultTransform = currentTransform;
        } else
        {
            defaultTransform = GetTransformFromMatrix4(jointGroup.DefaultPose.Matrix4);
        }
        skeleton.SetBonePose(boneIndex, currentTransform);
        skeleton.SetBoneRest(boneIndex, defaultTransform);
        skin.AddBind(boneIndex, currentTransform);
        skin.SetBindName(boneIndex, jointGroup.Name);
        if (parentIndex > -1) skeleton.SetBoneParent(boneIndex, parentIndex);

        foreach (var joint in jointGroup.Joints)
        {
            AddBonesToSkeleton(joint, ref skeleton, ref skin, boneIndex);
        }

        Transform3D GetTransformFromMatrix4(double[,] matrix4)
        {
            Vector3 basis_col1 = new Vector3((float)matrix4[0, 0], (float)matrix4[0, 1], (float)matrix4[0, 2]);
            Vector3 basis_col2 = new Vector3((float)matrix4[1, 0], (float)matrix4[1, 1], (float)matrix4[1, 2]);
            Vector3 basis_col3 = new Vector3((float)matrix4[2, 0], (float)matrix4[2, 1], (float)matrix4[2, 2]);
            Vector3 origin = new Vector3((float)matrix4[3, 0], (float)matrix4[3, 1], (float)matrix4[3, 2]);
            Basis matrixBasis = new Basis(basis_col1, basis_col2, basis_col3);
            var transform = new Transform3D(matrixBasis, origin);

            // In a million years...
            // In multiple universes...
            // Even with all of the world's knowledge...
            // I could NOT tell you why this fixes a major bug.
            // I. DO. NOT. UNDERSTAND. WHY.
            if(boneIndex == 0)
            {
                GD.Print(jointGroup.Name);
                transform = transform.Rotated(new Vector3(1, 0, 0), Mathf.DegToRad(-90));
            }

            return transform;
        }
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