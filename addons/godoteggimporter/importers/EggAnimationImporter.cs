using System;
using System.Linq;
using System.Text;

using Panda3DEggParser;

using Godot;
using Godot.Collections;
using System.Collections.Generic;

[Tool]
public partial class EggAnimationImporter : EditorImportPlugin
{
    public override string _GetImporterName()
    {
        return "autumnrivers.panda3d.egg_anim";
    }

    public override string _GetVisibleName()
    {
        return "Panda3D EGG Animation";
    }

    public override string[] _GetRecognizedExtensions()
    {
        return ["egg"];
    }

    public override string _GetSaveExtension()
    {
        return "anim";
    }

    public override string _GetResourceType()
    {
        return "Animation";
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
                // This will add the root bundle name to the nodepaths
                { "name", "use_blender_layout" },
                { "default_value", true }
            },
            new Dictionary()
            {
                { "name", "loop_animation" },
                { "default_value", false }
            },
            new Dictionary()
            {
                { "name", "apply_corporate_clash_fixes" },
                { "default_value", true }
            },
            new Dictionary()
            {
                { "name", "apply_very_stupid_fix" },
                { "default_value", false }
            }
        };
    }

    public override Error _Import(string sourceFile, string savePath, Dictionary options, Array<string> platformVariants, Array<string> genFiles)
    {
        EggParser parser;

        using var file = FileAccess.Open(sourceFile, FileAccess.ModeFlags.Read);
        if (file.GetError() != Error.Ok)
        {
            GD.Print(file.GetError());
            return Error.Failed;
        }

        parser = new(file.GetAsText());
        Panda3DEgg egg = parser.Parse();

        if(!egg.Data.Any(g => g is Table))
        {
            GD.PrintErr("EGG ANIMATION IMPORT FAILED: " +
                $"The file at {sourceFile} isn't an animation file. It's probably a model file.\n" +
                "Go to the file, and reimport it as 'Panda3D EGG Model'.");
#if DEBUG
            GD.Print("The egg animation import failed because there wasn't a <Table> group in the root.");
#endif
            return Error.Failed;
        }

#if DEBUG
        GD.PushWarning("Egg Animations are currently UNSUPPORTED! The animation importer is pretty broken, and doesn't " +
            "work right most of the time. If you'd like to help fix this, please do!\n" +
            "https://github.com/AutumnRivers/godot-egg-importer");
#endif

        Animation eggAnim = new();
        if ((bool)options["loop_animation"])
        {
            eggAnim.LoopMode = Animation.LoopModeEnum.Linear;
        }

        eggAnim.Step = 1 / 24;
        var success = ParseEggAnimation(egg, options, ref eggAnim);

        /*
         * h = heading = rotate around y-axis
         * r = roll = rotate around z-axis
         * p = pitch = rotate around x-axis
         * xyz = translation (swap y and z)
         * ijk = scale = xyz (swap y and z, again)
         */

        string filename = $"{savePath}.{_GetSaveExtension()}";
        var saver = ResourceSaver.Save(eggAnim, filename);
        return saver;
        // debug
        // return Error.Ok;
    }

    string rotValues = "hpr";
    string posValues = "xyz";

    private int _fps = 24;

    private bool ParseEggAnimation(Panda3DEgg egg, Dictionary options, ref Animation animation)
    {
        // Animations usually only have one root table. We can go off this rule
        StringBuilder nodePath = new();
        var tableData = ((Table)egg.Data.First(g => g is Table)).Bundles;
        var rootBundle = tableData[0];

        if ((bool)options["use_blender_layout"])
        {
            string rootName = rootBundle.Name;
            if ((bool)options["apply_corporate_clash_fixes"])
            {
                // Corporate Clash animation fixes
                /*
                 * Corporate Clash animations have malformed structures due to... something? Not sure.
                 * This can be easily fixed by changing the root bundle name, and so that's what we do here.
                 * 
                 * AGAIN: You should not use Corporate Clash's models without their express permission if you
                 * plan to distribute your project's files. Things such as video animations, however, tend to be allowed.
                 */
                if (rootBundle.Name == "Fk.rig")
                {
                    rootName = rootBundle.Tables[0].Tables[0].Name;
                }
                if (rootBundle.Name.EndsWith(".001"))
                {
                    rootName = rootBundle.Name.Split(".001")[0];
                }
            }
            nodePath.Append(rootName);
            nodePath.Append('/');
        }

        nodePath.Append("Skeleton3D");
        nodePath.Append(':');

        foreach(var table in rootBundle.Tables)
        {
            ParseAnimationTable(table, ref animation);
        }

        string baseTrack = string.Empty;
        string currentTrack = string.Empty;

        void ParseAnimationTable(Table animTable, ref Animation anim)
        {
            if (animTable.Name == "<skeleton>" || animTable.Name == "morph") return;

            baseTrack = string.Empty;
            string nPath = nodePath.ToString() + animTable.Name;
            baseTrack = nPath;

            if ((bool)options["apply_very_stupid_fix"])
            {
                baseTrack += "_2";
            }

            var xfms = animTable.Animations.Where(a => a is XfmAnimationS);
            if(xfms.Any())
            {
                foreach(XfmAnimationS xfm_s in xfms)
                {
                    ParseSxfmAnimation(xfm_s, ref anim);
                }
            }

            foreach(var table in animTable.Tables)
            {
                ParseAnimationTable(table, ref anim);
            }
        }

        void ParseSxfmAnimation(XfmAnimationS xfmanims, ref Animation anim)
        {
            /*foreach (var sanim in xfmanims.Animations)
            {
                ParseSAnimation(sanim, ref anim);
            }*/
            if(xfmanims.Animations.Any(s => posValues.Contains(s.Variable)))
            {
                ParseSAnimationPosition(xfmanims.Animations.Where(s => posValues.Contains(s.Variable)).ToList(), ref anim);
            }
            if(xfmanims.Animations.Any(s => rotValues.Contains(s.Variable)))
            {
                ParseSAnimationRotation(xfmanims.Animations.Where(s => rotValues.Contains(s.Variable)).ToList(), ref anim);
            }
        }

        void ParseSAnimationPosition(List<SAnimation> sanims, ref Animation anim)
        {
            double keyfrag = 1.0 / 24.0;
            int keyIdx = 0;
            int trackIndex = anim.AddTrack(Animation.TrackType.Position3D);
            anim.TrackSetPath(trackIndex, baseTrack);
            anim.Step = (float)keyfrag;
            
            var xanims = sanims.Where(a => a.Variable == 'x').ToList();
            List<SAnimation> yanims;
            List<SAnimation> zanims;

            bool isFirstTrack = false;

            if (anim.TrackGetPath(0) != baseTrack)
            {
                // we're not on the first path. don't make modifications
                yanims = sanims.Where(a => a.Variable == 'y').ToList();
                zanims = sanims.Where(a => a.Variable == 'z').ToList();
            } else
            {
                yanims = sanims.Where(a => a.Variable == 'z').ToList();
                zanims = sanims.Where(a => a.Variable == 'y').ToList();
                isFirstTrack = true;
            }

            yanims = sanims.Where(a => a.Variable == 'z').ToList();
            zanims = sanims.Where(a => a.Variable == 'y').ToList();

            int xvalues = xanims.Count > 0 ? xanims[0].Values.Count : -1;
            int yvalues = yanims.Count > 0 ? yanims[0].Values.Count : -1;
            int zvalues = zanims.Count > 0 ? zanims[0].Values.Count : -1;
            int maxanims = Mathf.Max(Mathf.Max(xvalues, yvalues), zvalues);

            anim.Length = Mathf.Max(anim.Length, (float)keyfrag * maxanims);

            float lastX = 0f;
            float lastY = 0f;
            float lastZ = 0f;
            for(int i = 0; i < maxanims; i++)
            {
                float x = lastX;
                float y = lastY;
                float z = lastZ;
                if(xanims.Count > 0)
                {
                    var xanim = xanims[0];
                    if(xanim.Values.Count > i)
                    {
                        x = (float)xanim.Values[i];
                        lastX = x;
                    }
                }
                if (yanims.Count > 0)
                {
                    var yanim = yanims[0];
                    if (yanim.Values.Count > i)
                    {
                        y = (float)yanim.Values[i];
                        lastY = y;
                    }
                }
                if (zanims.Count > 0)
                {
                    var zanim = zanims[0];
                    if (zanim.Values.Count > i)
                    {
                        z = (float)zanim.Values[i];
                        if (isFirstTrack) z *= -1;
                        lastZ = z;
                    }
                }
                Vector3 pos = new(x, y, z);
                if(anim.TrackGetKeyCount(trackIndex) > 0)
                {
                    var previousKey = (Vector3)anim.TrackGetKeyValue(trackIndex, keyIdx);
                    if (pos == previousKey) continue;
                }
                keyIdx = anim.PositionTrackInsertKey(trackIndex, keyfrag * i, pos);
            }
        }

        var pitchAxis = new Vector3(1, 0, 0);

        void ParseSAnimationRotation(List<SAnimation> sanims, ref Animation anim)
        {
            int keyIndex = 0;
            double keyfrag = 1.0 / 24.0;
            int trackIndex = anim.AddTrack(Animation.TrackType.Rotation3D);
            anim.TrackSetPath(trackIndex, baseTrack);
            anim.Step = (float)keyfrag;

            var pitchValues = sanims.Where(a => a.Variable == 'p').ToList();
            List<SAnimation> headingValues = sanims.Where(a => a.Variable == 'h').ToList();
            List<SAnimation> rollValues = sanims.Where(a => a.Variable == 'r').ToList();

            bool isFirstTrack = false;

            if (anim.TrackGetPath(0) == baseTrack)
            {
                isFirstTrack = true;
            }

            int pitchCount = pitchValues.Count > 0 ? pitchValues[0].Values.Count : -1;
            int headingCount = headingValues.Count > 0 ? headingValues[0].Values.Count : -1;
            int rollCount = rollValues.Count > 0 ? rollValues[0].Values.Count : -1;

            int maxanims = Mathf.Max(Mathf.Max(pitchCount, headingCount), rollCount);

            anim.Length = Mathf.Max(anim.Length, (float)keyfrag * maxanims);

            float lastPitch = 0.0f;
            float lastHead = 0.0f;
            float lastRoll = 0.0f;
            for(int i = 0; i < maxanims; i++)
            {
                float pitch = lastPitch;
                float heading = lastHead;
                float roll = lastRoll;

                if(pitchValues.Count > 0)
                {
                    var pitchFrame = pitchValues[0];
                    if(pitchFrame.Values.Count > i)
                    {
                        pitch = (float)pitchFrame.Values[i];
                        lastPitch = pitch;
                    }
                }
                if (headingValues.Count > 0)
                {
                    var headFrame = headingValues[0];
                    if (headFrame.Values.Count > i)
                    {
                        heading = (float)headFrame.Values[i];
                        lastHead = heading;
                    }
                }
                if (rollValues.Count > 0)
                {
                    var rollFrame = rollValues[0];
                    if (rollFrame.Values.Count > i)
                    {
                        roll = (float)rollFrame.Values[i];
                        lastRoll = roll;
                    }
                }

                if (baseTrack.EndsWith("joint4")) GD.Print($"Roll/Y: {roll}");
                if (isFirstTrack) roll -= 110; // ??? I don't know why I need to do this, but I do.

                if (baseTrack.EndsWith("joint4")) GD.Print($"Euler (Degrees) (X: {pitch} / Y: {roll} / Z: {heading})");
                pitch = Mathf.DegToRad(pitch);
                heading = Mathf.DegToRad(heading);
                roll = Mathf.DegToRad(roll);
                Quaternion rotation = Quaternion.FromEuler(new Vector3(roll, pitch, heading));
                // STUPID FIX ALERT // STUPID FIX ALERT //
                var quatY = rotation.Y;
                rotation.Y = rotation.X;
                rotation.X = quatY;
                rotation = rotation.Normalized();
                // END STUPID FIX // END STUPID FIX //
                // YES, THIS WORKS. NO, I DON'T KNOW WHY
                if(isFirstTrack) rotation *= new Quaternion(new Vector3(0, 1, 0), Mathf.DegToRad(180));
                if (baseTrack.EndsWith("joint4")) GD.Print($"Euler (Radians) (X: {pitch} / Y: {roll} / Z: {heading})");
                if(anim.TrackGetKeyCount(trackIndex) > 0)
                {
                    var previousKey = (Quaternion)anim.TrackGetKeyValue(trackIndex, keyIndex);
                    if (previousKey == rotation) continue;
                }
                keyIndex = anim.RotationTrackInsertKey(trackIndex, keyfrag * i, rotation);
            }
        }

        void ParseSAnimation(SAnimation sanim, ref Animation anim)
        {
            int key_index = 0;
            int trackIndex = -1;

            float key_fragment = 1f / 24f;

            if((sanim.Values.Count * key_fragment) > anim.Length)
            {
                anim.Length = sanim.Values.Count * key_fragment;
            }

            if (rotValues.Contains(sanim.Variable))
            {
                // parse rotation values...
                Vector3 axis;
                char rotChar = 'h';

                switch (sanim.Variable)
                {
                    case 'h':
                        axis = new Vector3(0, 1, 0);
                        rotChar = 'h';
                        break;
                    case 'r':
                        axis = new Vector3(0, 0, 1);
                        rotChar = 'r';
                        break;
                    case 'p':
                        axis = new Vector3(1, 0, 0);
                        rotChar = 'p';
                        break;
                    default:
                        GD.PrintErr("!? Impossible error occured !?");
                        axis = Vector3.Zero;
                        break;
                }

                trackIndex = anim.FindTrack(baseTrack, Animation.TrackType.Rotation3D);
                if (trackIndex == -1)
                {
                    trackIndex = anim.AddTrack(Animation.TrackType.Rotation3D);
                    anim.TrackSetPath(trackIndex, baseTrack);
                }
                for (int i = 0; i < sanim.Values.Count; i++)
                {
                    bool isFirstTrack = true;
                    if(anim.GetTrackCount() > 0)
                    {
                        var firstTrack = anim.TrackGetPath(0);
                        var firstTrackNode = firstTrack.ToString();
                        if(baseTrack != firstTrackNode)
                        {
                            isFirstTrack = false;
                            if (axis == new Vector3(0, 1, 0) && rotChar == 'h')
                            {
                                axis = new Vector3(0, 0, 1);
                            } else if(axis == new Vector3(0,0,1) && rotChar == 'r')
                            {
                                axis = new Vector3(0, 1, 0);
                            }
                        }
                    }
                    if (anim.TrackGetKeyCount(trackIndex) <= i || anim.TrackGetKeyCount(trackIndex) == 0)
                    {
                        // I DON'T KNOW HOW TO FIX THIS
                        // I REALLY DO NOT
                        float fragment_mult = i;
                        if (anim.TrackGetKeyCount(trackIndex) == 1) fragment_mult = 1;
                        float angle = Mathf.DegToRad((float)sanim.Values[i]);
                        Quaternion value = new Quaternion(axis, angle);
                        if(isFirstTrack)
                        {
                            value *= new Quaternion(new Vector3(1, 0, 0), 90);
                        }
                        key_index = anim.TrackInsertKey(trackIndex, key_fragment * fragment_mult, value);
                    }
                    else
                    {
                        Quaternion currentRotationValue = (Quaternion)anim.TrackGetKeyValue(trackIndex, key_index);
                        float angle = Mathf.DegToRad((float)sanim.Values[i]);
                        Quaternion rotationValue = new(axis, angle);
                        anim.TrackSetKeyValue(trackIndex, key_index, currentRotationValue * rotationValue);
                        key_index++;
                    }
                }

            } else if (posValues.Contains(sanim.Variable))
            {
                // parse translation values...
                Vector3 translation;
                char translationChar;

                switch (sanim.Variable)
                {
                    case 'x':
                        translation = new Vector3(1, 0, 0);
                        translationChar = 'x';
                        break;
                    case 'y':
                        translation = new Vector3(0, 0, -1);
                        translationChar = 'y';
                        break;
                    case 'z':
                        translation = new Vector3(0, 1, 0);
                        translationChar = 'z';
                        break;
                    default:
                        GD.PrintErr("!? Impossible error occured !?");
                        translation = Vector3.Zero;
                        translationChar = 'x';
                        break;
                }

                trackIndex = anim.FindTrack(baseTrack, Animation.TrackType.Position3D);
                if (trackIndex == -1)
                {
                    trackIndex = anim.AddTrack(Animation.TrackType.Position3D);
                    anim.TrackSetPath(trackIndex, baseTrack);
                }
                for (int i = 0; i < sanim.Values.Count; i++)
                {
                    if(anim.GetTrackCount() > 0)
                    {
                        var firstTrack = anim.TrackGetPath(0);
                        var firstTrackNode = firstTrack.ToString();
                        if(baseTrack != firstTrackNode)
                        {
                            if(translation == new Vector3(0, 0, -1) && translationChar == 'y')
                            {
                                translation = new Vector3(0, 1, 0);
                            } else if(translation == new Vector3(0, 1, 0) && translationChar == 'z')
                            {
                                translation = new Vector3(0, 0, 1);
                            }
                        }
                    }
                    if (anim.TrackGetKeyCount(trackIndex) <= i)
                    {
                        float fragment_mult = i;
                        if (anim.TrackGetKeyCount(trackIndex) == 1) fragment_mult = 1;
                        key_index = anim.TrackInsertKey(trackIndex, key_fragment * fragment_mult, translation * (float)sanim.Values[i]);
                    }
                    else
                    {
                        Vector3 currentTranslation = (Vector3)anim.TrackGetKeyValue(trackIndex, key_index);
                        Vector3 newTranslation = translation * (float)sanim.Values[i];
                        Vector3 combinedTranslation = currentTranslation + newTranslation;
                        if(key_index > 0)
                        {
                            Vector3 previousKeyValue = (Vector3)anim.TrackGetKeyValue(trackIndex, key_index - 1);
                            if (baseTrack.EndsWith("joint18"))
                            {
                                GD.Print($"Prev Y: {previousKeyValue.Y} / New Y: {combinedTranslation.Y}" +
                                    $" / {previousKeyValue.Y != 0} / {combinedTranslation.Y == 0 || combinedTranslation.Y == -0}");
                            }
                            if (previousKeyValue.X != 0 && combinedTranslation.X == 0) combinedTranslation.X = previousKeyValue.X;
                            if (previousKeyValue.Y != 0 && combinedTranslation.Y == 0) combinedTranslation.Y = previousKeyValue.Y;
                            if (previousKeyValue.Z != 0 && combinedTranslation.Z == 0) combinedTranslation.Z = previousKeyValue.Z;
                        }
                        anim.TrackSetKeyValue(trackIndex, key_index, combinedTranslation);
                        key_index++;
                    }
                }
            } else
            {
                GD.PushWarning("EGG ANIMATION IMPORT WARN: " +
                    $"Animations with the {sanim.Variable} variable aren't currently supported. " +
                    "Skipping...");
            }
        }

        return true;
    }
}