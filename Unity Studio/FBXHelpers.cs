using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace UnityStudio
{
    public partial class UnityStudioForm
    {
        private void WriteFBX(string FBXfile, bool allNodes)
        {
            DateTime timestamp = DateTime.Now;

            using (StreamWriter FBXwriter = new StreamWriter(FBXfile))
            {
                StringBuilder fbx = new StringBuilder();
                StringBuilder ob = new StringBuilder(); //Objects builder
                StringBuilder cb = new StringBuilder(); //Connections builder
                StringBuilder mb = new StringBuilder(); //Materials builder to get texture count in advance
                StringBuilder cb2 = new StringBuilder(); //and keep connections ordered
                cb.Append("\n}\n"); //Objects end
                cb.Append("\nConnections:  {");

                HashSet<GameObject> GameObjects = new HashSet<GameObject>();
                HashSet<GameObject> LimbNodes = new HashSet<GameObject>();
                HashSet<AssetPreloadData> Skins = new HashSet<AssetPreloadData>();
                HashSet<AssetPreloadData> Meshes = new HashSet<AssetPreloadData>(); //MeshFilters are not unique!!
                HashSet<AssetPreloadData> Materials = new HashSet<AssetPreloadData>();
                HashSet<AssetPreloadData> Textures = new HashSet<AssetPreloadData>();

                int DeformerCount = 0;
                // uniqueIDs can begin with zero, so they are preceded by a number specific to their type
                // this will also make it easier to debug FBX files
                // 1: Model
                // 2: NodeAttribute
                // 3: Geometry
                // 4: Deformer
                // 5: CollectionExclusive
                // 6: Material
                // 7: Texture
                // 8: Video
                // 9:

                #region loop nodes and collect objects for export

                foreach (AssetsFile assetsFile in assetsfileList)
                {
                    foreach (GameObject m_GameObject in assetsFile.GameObjectList.Values)
                    {
                        if (!m_GameObject.Checked && !allNodes)
                            continue;

                        GameObjects.Add(m_GameObject);

                        AssetPreloadData MeshFilterPD;
                        if (assetsfileList.TryGetPD(m_GameObject.m_MeshFilter, out MeshFilterPD))
                        {
                            //MeshFilters are not unique!
                            //MeshFilters.Add(MeshFilterPD);
                            MeshFilter m_MeshFilter = new MeshFilter(MeshFilterPD);
                            AssetPreloadData MeshPD;
                            if (assetsfileList.TryGetPD(m_MeshFilter.m_Mesh, out MeshPD))
                            {
                                Meshes.Add(MeshPD);

                                // write connections here and Mesh objects separately without having to backtrack
                                // through their MEshFilter to het the GameObject ID also note that MeshFilters
                                // are not unique, they cannot be used for instancing geometry
                                cb2.AppendFormat("\n\n\t;Geometry::, Model::{0}", m_GameObject.m_Name);
                                cb2.AppendFormat("\n\tC: \"OO\",3{0},1{1}", MeshPD.uniqueID, m_GameObject.uniqueID);
                            }
                        }

                        #region get Renderer

                        AssetPreloadData RendererPD;
                        if (assetsfileList.TryGetPD(m_GameObject.m_Renderer, out RendererPD))
                        {
                            Renderer m_Renderer = new Renderer(RendererPD);

                            foreach (PPtr MaterialPPtr in m_Renderer.m_Materials)
                            {
                                AssetPreloadData MaterialPD;
                                if (assetsfileList.TryGetPD(MaterialPPtr, out MaterialPD))
                                {
                                    Materials.Add(MaterialPD);
                                    cb2.AppendFormat("\n\n\t;Material::, Model::{0}", m_GameObject.m_Name);
                                    cb2.AppendFormat("\n\tC: \"OO\",6{0},1{1}", MaterialPD.uniqueID,
                                        m_GameObject.uniqueID);
                                }
                            }
                        }

                        #endregion

                        #region get SkinnedMeshRenderer

                        AssetPreloadData SkinnedMeshPD;
                        if (!assetsfileList.TryGetPD(m_GameObject.m_SkinnedMeshRenderer, out SkinnedMeshPD))
                            continue;

                        Skins.Add(SkinnedMeshPD);

                        SkinnedMeshRenderer m_SkinnedMeshRenderer = new SkinnedMeshRenderer(SkinnedMeshPD);

                        foreach (PPtr MaterialPPtr in m_SkinnedMeshRenderer.m_Materials)
                        {
                            AssetPreloadData MaterialPD;
                            if (assetsfileList.TryGetPD(MaterialPPtr, out MaterialPD))
                            {
                                Materials.Add(MaterialPD);
                                cb2.AppendFormat("\n\n\t;Material::, Model::{0}", m_GameObject.m_Name);
                                cb2.AppendFormat("\n\tC: \"OO\",6{0},1{1}", MaterialPD.uniqueID,
                                    m_GameObject.uniqueID);
                            }
                        }

                        if (!(bool) Properties.Settings.Default["exportDeformers"])
                            continue;

                        DeformerCount += m_SkinnedMeshRenderer.m_Bones.Length;

                        //collect skeleton dummies to make sure they are exported
                        foreach (PPtr bonePPtr in m_SkinnedMeshRenderer.m_Bones)
                        {
                            Transform b_Transform;
                            if (!assetsfileList.TryGetTransform(bonePPtr, out b_Transform))
                                continue;

                            GameObject m_Bone;
                            if (assetsfileList.TryGetGameObject(b_Transform.m_GameObject, out m_Bone))
                            {
                                LimbNodes.Add(m_Bone);
                                //also collect the root bone
                                if (m_Bone.Parent.Level > 0)
                                {
                                    LimbNodes.Add((GameObject) m_Bone.Parent);
                                }
                                //should I collect siblings?
                            }

                            #region collect children because m_SkinnedMeshRenderer.m_Bones doesn't contain terminations

                            foreach (PPtr ChildPPtr in b_Transform.m_Children)
                            {
                                Transform ChildTR;
                                if (!assetsfileList.TryGetTransform(ChildPPtr, out ChildTR))
                                    continue;

                                GameObject m_Child;
                                if (!assetsfileList.TryGetGameObject(ChildTR.m_GameObject, out m_Child))
                                    continue;

                                // check that the Model doesn't contain a Mesh,
                                // although this won't ensure it's part of the skeleton
                                if (m_Child.m_MeshFilter == null && m_Child.m_SkinnedMeshRenderer == null)
                                {
                                    LimbNodes.Add(m_Child);
                                }
                            }
                            #endregion
                        }
                        #endregion
                    }
                }

                GameObjects.UnionWith(LimbNodes);

                #endregion

                #region write Materials, collect Texture objects

                StatusStripUpdate("Writing Materials");
                foreach (AssetPreloadData MaterialPD in Materials)
                {
                    Material m_Material = new Material(MaterialPD);

                    mb.AppendFormat("\n\tMaterial: 6{0}, \"Material::{1}\", \"\" {{", MaterialPD.uniqueID,
                        m_Material.m_Name);
                    mb.Append("\n\t\tVersion: 102");
                    mb.Append("\n\t\tShadingModel: \"phong\"");
                    mb.Append("\n\t\tMultiLayer: 0");
                    mb.Append("\n\t\tProperties70:  {");
                    mb.Append("\n\t\t\tP: \"ShadingModel\", \"KString\", \"\", \"\", \"phong\"");

                    #region write material colors

                    bool hasSpecualar = false;
                    foreach (StrColorPair m_Color in m_Material.m_Colors)
                    {
                        switch (m_Color.first)
                        {
                            case "_Color":
                            case "gSurfaceColor":
                                mb.AppendFormat("\n\t\t\tP: \"DiffuseColor\", \"Color\", \"\", \"A\",{0},{1},{2}",
                                    m_Color.second[0], m_Color.second[1], m_Color.second[2]);
                                break;
                            case "_SpecularColor": //then what is _SpecColor??
                                mb.AppendFormat("\n\t\t\tP: \"SpecularColor\", \"Color\", \"\", \"A\",{0},{1},{2}",
                                    m_Color.second[0], m_Color.second[1], m_Color.second[2]);
                                hasSpecualar = true;
                                break;
                            case "_ReflectColor":
                                mb.AppendFormat("\n\t\t\tP: \"AmbientColor\", \"Color\", \"\", \"A\",{0},{1},{2}",
                                    m_Color.second[0], m_Color.second[1], m_Color.second[2]);
                                break;
                            default:
                                mb.AppendFormat("\n;\t\t\tP: \"{3}\", \"Color\", \"\", \"A\",{0},{1},{2}",
                                    m_Color.second[0], m_Color.second[1], m_Color.second[2],
                                    m_Color.first); //commented out
                                break;
                        }
                    }
                    if (!hasSpecualar)
                        mb.Append("\n\t\t\tP: \"SpecularColor\", \"Color\", \"\", \"A\",0,0,0");

                    #endregion

                    #region write material parameters

                    foreach (StrFloatPair m_Float in m_Material.m_Floats)
                    {
                        switch (m_Float.first)
                        {
                            case "_Shininess":
                                mb.AppendFormat("\n\t\t\tP: \"ShininessExponent\", \"Number\", \"\", \"A\",{0}",
                                    m_Float.second);
                                mb.AppendFormat("\n\t\t\tP: \"Shininess\", \"Number\", \"\", \"A\",{0}",
                                    m_Float.second);
                                break;
                            case "_Transparency":
                                mb.Append("\n\t\t\tP: \"TransparentColor\", \"Color\", \"\", \"A\",1,1,1");
                                mb.AppendFormat("\n\t\t\tP: \"TransparencyFactor\", \"Number\", \"\", \"A\",{0}",
                                    m_Float.second);
                                mb.AppendFormat("\n\t\t\tP: \"Opacity\", \"Number\", \"\", \"A\",{0}",
                                    1 - m_Float.second);
                                break;
                            default:
                                mb.AppendFormat("\n;\t\t\tP: \"{0}\", \"Number\", \"\", \"A\",{1}",
                                    m_Float.first, m_Float.second);
                                break;
                        }
                    }

                    #endregion

                    //mb.Append("\n\t\t\tP: \"SpecularFactor\", \"Number\", \"\", \"A\",0");
                    mb.Append("\n\t\t}");
                    mb.Append("\n\t}");

                    #region write texture connections

                    foreach (TexEnv m_TexEnv in m_Material.m_TexEnvs)
                    {
                        AssetPreloadData TexturePD;

                        #region get Porsche material from json

                        if (!assetsfileList.TryGetPD(m_TexEnv.m_Texture, out TexturePD) && jsonMats != null)
                        {
                            Dictionary<string, string> matProp;
                            if (jsonMats.TryGetValue(m_Material.m_Name, out matProp))
                            {
                                string texName;
                                if (matProp.TryGetValue(m_TexEnv.name, out texName))
                                {
                                    foreach (AssetPreloadData asset in exportableAssets)
                                    {
                                        if (asset.Type2 == 28 && asset.Text == texName)
                                        {
                                            TexturePD = asset;
                                            break;
                                        }
                                    }
                                }
                            }
                        }

                        #endregion

                        if (TexturePD != null && TexturePD.Type2 == 28)
                        {
                            Textures.Add(TexturePD);

                            cb2.AppendFormat("\n\n\t;Texture::, Material::{0}", m_Material.m_Name);
                            cb2.AppendFormat("\n\tC: \"OP\",7{0},6{1}, \"", TexturePD.uniqueID, MaterialPD.uniqueID);

                            switch (m_TexEnv.name)
                            {
                                case "_MainTex":
                                case "gDiffuseSampler":
                                    cb2.Append("DiffuseColor\"");
                                    break;
                                case "_SpecularMap":
                                case "gSpecularSampler":
                                    cb2.Append("SpecularColor\"");
                                    break;
                                case "_NormalMap":
                                case "gNormalSampler":
                                    cb2.Append("NormalMap\"");
                                    break;
                                case "_BumpMap":
                                    cb2.Append("Bump\"");
                                    break;
                                default:
                                    cb2.AppendFormat("{0}\"", m_TexEnv.name);
                                    break;
                            }
                        }
                    }

                    #endregion
                }

                #endregion

                #region write generic FBX data after everything was collected

                fbx.Append("; FBX 7.1.0 project file");
                fbx.Append(
                    "\nFBXHeaderExtension:  {\n\tFBXHeaderVersion: 1003\n\tFBXVersion: 7100\n\tCreationTimeStamp:  {\n\t\tVersion: 1000");
                fbx.Append("\n\t\tYear: " + timestamp.Year);
                fbx.Append("\n\t\tMonth: " + timestamp.Month);
                fbx.Append("\n\t\tDay: " + timestamp.Day);
                fbx.Append("\n\t\tHour: " + timestamp.Hour);
                fbx.Append("\n\t\tMinute: " + timestamp.Minute);
                fbx.Append("\n\t\tSecond: " + timestamp.Second);
                fbx.Append("\n\t\tMillisecond: " + timestamp.Millisecond);
                fbx.Append("\n\t}\n\tCreator: \"Unity Studio by Chipicao\"\n}\n");

                fbx.Append("\nGlobalSettings:  {");
                fbx.Append("\n\tVersion: 1000");
                fbx.Append("\n\tProperties70:  {");
                fbx.Append("\n\t\tP: \"UpAxis\", \"int\", \"Integer\", \"\",1");
                fbx.Append("\n\t\tP: \"UpAxisSign\", \"int\", \"Integer\", \"\",1");
                fbx.Append("\n\t\tP: \"FrontAxis\", \"int\", \"Integer\", \"\",2");
                fbx.Append("\n\t\tP: \"FrontAxisSign\", \"int\", \"Integer\", \"\",1");
                fbx.Append("\n\t\tP: \"CoordAxis\", \"int\", \"Integer\", \"\",0");
                fbx.Append("\n\t\tP: \"CoordAxisSign\", \"int\", \"Integer\", \"\",1");
                fbx.Append("\n\t\tP: \"OriginalUpAxis\", \"int\", \"Integer\", \"\",1");
                fbx.Append("\n\t\tP: \"OriginalUpAxisSign\", \"int\", \"Integer\", \"\",1");
                fbx.AppendFormat("\n\t\tP: \"UnitScaleFactor\", \"double\", \"Number\", \"\",{0}",
                    Properties.Settings.Default["scaleFactor"]);
                fbx.Append("\n\t\tP: \"OriginalUnitScaleFactor\", \"double\", \"Number\", \"\",1.0");
                fbx.Append("\n\t}\n}\n");

                fbx.Append("\nDocuments:  {");
                fbx.Append("\n\tCount: 1");
                fbx.Append("\n\tDocument: 1234567890, \"\", \"Scene\" {");
                fbx.Append("\n\t\tProperties70:  {");
                fbx.Append("\n\t\t\tP: \"SourceObject\", \"object\", \"\", \"\"");
                fbx.Append("\n\t\t\tP: \"ActiveAnimStackName\", \"KString\", \"\", \"\", \"\"");
                fbx.Append("\n\t\t}");
                fbx.Append("\n\t\tRootNode: 0");
                fbx.Append("\n\t}\n}\n");
                fbx.Append("\nReferences:  {\n}\n");

                fbx.Append("\nDefinitions:  {");
                fbx.Append("\n\tVersion: 100");
                fbx.AppendFormat("\n\tCount: {0}",
                    1 + 2 * GameObjects.Count + Materials.Count + 2 * Textures.Count +
                    ((bool) Properties.Settings.Default["exportDeformers"]
                        ? Skins.Count + DeformerCount + Skins.Count + 1
                        : 0));

                fbx.Append("\n\tObjectType: \"GlobalSettings\" {");
                fbx.Append("\n\t\tCount: 1");
                fbx.Append("\n\t}");

                fbx.Append("\n\tObjectType: \"Model\" {");
                fbx.AppendFormat("\n\t\tCount: {0}", GameObjects.Count);
                fbx.Append("\n\t}");

                fbx.Append("\n\tObjectType: \"NodeAttribute\" {");
                fbx.AppendFormat("\n\t\tCount: {0}", GameObjects.Count - Meshes.Count - Skins.Count);
                fbx.Append("\n\t\tPropertyTemplate: \"FbxNull\" {");
                fbx.Append("\n\t\t\tProperties70:  {");
                fbx.Append("\n\t\t\t\tP: \"Color\", \"ColorRGB\", \"Color\", \"\",0.8,0.8,0.8");
                fbx.Append("\n\t\t\t\tP: \"Size\", \"double\", \"Number\", \"\",100");
                fbx.Append("\n\t\t\t\tP: \"Look\", \"enum\", \"\", \"\",1");
                fbx.Append("\n\t\t\t}\n\t\t}\n\t}");

                fbx.Append("\n\tObjectType: \"Geometry\" {");
                fbx.AppendFormat("\n\t\tCount: {0}", Meshes.Count + Skins.Count);
                fbx.Append("\n\t}");

                fbx.Append("\n\tObjectType: \"Material\" {");
                fbx.AppendFormat("\n\t\tCount: {0}", Materials.Count);
                fbx.Append("\n\t}");

                fbx.Append("\n\tObjectType: \"Texture\" {");
                fbx.AppendFormat("\n\t\tCount: {0}", Textures.Count);
                fbx.Append("\n\t}");

                fbx.Append("\n\tObjectType: \"Video\" {");
                fbx.AppendFormat("\n\t\tCount: {0}", Textures.Count);
                fbx.Append("\n\t}");

                if ((bool) Properties.Settings.Default["exportDeformers"])
                {
                    fbx.Append("\n\tObjectType: \"CollectionExclusive\" {");
                    fbx.AppendFormat("\n\t\tCount: {0}", Skins.Count);
                    fbx.Append("\n\t\tPropertyTemplate: \"FbxDisplayLayer\" {");
                    fbx.Append("\n\t\t\tProperties70:  {");
                    fbx.Append("\n\t\t\t\tP: \"Color\", \"ColorRGB\", \"Color\", \"\",0.8,0.8,0.8");
                    fbx.Append("\n\t\t\t\tP: \"Show\", \"bool\", \"\", \"\",1");
                    fbx.Append("\n\t\t\t\tP: \"Freeze\", \"bool\", \"\", \"\",0");
                    fbx.Append("\n\t\t\t\tP: \"LODBox\", \"bool\", \"\", \"\",0");
                    fbx.Append("\n\t\t\t}");
                    fbx.Append("\n\t\t}");
                    fbx.Append("\n\t}");

                    fbx.Append("\n\tObjectType: \"Deformer\" {");
                    fbx.AppendFormat("\n\t\tCount: {0}", DeformerCount + Skins.Count);
                    fbx.Append("\n\t}");

                    fbx.Append("\n\tObjectType: \"Pose\" {");
                    fbx.Append("\n\t\tCount: 1");
                    fbx.Append("\n\t}");
                }

                fbx.Append("\n}\n");
                fbx.Append("\nObjects:  {");

                FBXwriter.Write(fbx);
                fbx.Clear();

                #endregion

                #region write Model nodes and connections

                StatusStripUpdate("Writing Nodes and hierarchy");
                foreach (GameObject m_GameObject in GameObjects)
                {
                    if (m_GameObject.m_MeshFilter == null && m_GameObject.m_SkinnedMeshRenderer == null)
                    {
                        if ((bool) Properties.Settings.Default["exportDeformers"] &&
                            (bool) Properties.Settings.Default["convertDummies"] && LimbNodes.Contains(m_GameObject))
                        {
                            ob.AppendFormat("\n\tNodeAttribute: 2{0}, \"NodeAttribute::\", \"LimbNode\" {{",
                                m_GameObject.uniqueID);
                            ob.Append("\n\t\tTypeFlags: \"Skeleton\"");
                            ob.Append("\n\t}");

                            ob.AppendFormat("\n\tModel: 1{0}, \"Model::{1}\", \"LimbNode\" {{", m_GameObject.uniqueID,
                                m_GameObject.m_Name);
                        }
                        else
                        {
                            ob.AppendFormat("\n\tNodeAttribute: 2{0}, \"NodeAttribute::\", \"Null\" {{",
                                m_GameObject.uniqueID);
                            ob.Append("\n\t\tTypeFlags: \"Null\"");
                            ob.Append("\n\t}");

                            ob.AppendFormat("\n\tModel: 1{0}, \"Model::{1}\", \"Null\" {{", m_GameObject.uniqueID,
                                m_GameObject.m_Name);
                        }

                        //connect NodeAttribute to Model
                        cb.AppendFormat("\n\n\t;NodeAttribute::, Model::{0}", m_GameObject.m_Name);
                        cb.AppendFormat("\n\tC: \"OO\",2{0},1{0}", m_GameObject.uniqueID);
                    }
                    else
                    {
                        ob.AppendFormat("\n\tModel: 1{0}, \"Model::{1}\", \"Mesh\" {{", m_GameObject.uniqueID,
                            m_GameObject.m_Name);
                    }

                    ob.Append("\n\t\tVersion: 232");
                    ob.Append("\n\t\tProperties70:  {");
                    ob.Append("\n\t\t\tP: \"InheritType\", \"enum\", \"\", \"\",1");
                    ob.Append("\n\t\t\tP: \"ScalingMax\", \"Vector3D\", \"Vector\", \"\",0,0,0");
                    ob.Append("\n\t\t\tP: \"DefaultAttributeIndex\", \"int\", \"Integer\", \"\",0");

                    Transform m_Transform;
                    if (assetsfileList.TryGetTransform(m_GameObject.m_Transform, out m_Transform))
                    {
                        float[] m_EulerRotation = QuatToEuler(new []
                        {
                            m_Transform.m_LocalRotation[0], -m_Transform.m_LocalRotation[1],
                            -m_Transform.m_LocalRotation[2], m_Transform.m_LocalRotation[3]
                        });

                        ob.AppendFormat("\n\t\t\tP: \"Lcl Translation\", \"Lcl Translation\", \"\", \"A\",{0},{1},{2}",
                            -m_Transform.m_LocalPosition[0], m_Transform.m_LocalPosition[1],
                            m_Transform.m_LocalPosition[2]);
                        ob.AppendFormat("\n\t\t\tP: \"Lcl Rotation\", \"Lcl Rotation\", \"\", \"A\",{0},{1},{2}",
                            m_EulerRotation[0], m_EulerRotation[1],
                            m_EulerRotation[2]); //handedness is switched in quat
                        ob.AppendFormat("\n\t\t\tP: \"Lcl Scaling\", \"Lcl Scaling\", \"\", \"A\",{0},{1},{2}",
                            m_Transform.m_LocalScale[0], m_Transform.m_LocalScale[1], m_Transform.m_LocalScale[2]);
                    }

                    //mb.Append("\n\t\t\tP: \"UDP3DSMAX\", \"KString\", \"\", \"U\", \"MapChannel:1 = UVChannel_1&cr;&lf;MapChannel:2 = UVChannel_2&cr;&lf;\"");
                    //mb.Append("\n\t\t\tP: \"MaxHandle\", \"int\", \"Integer\", \"UH\",24");
                    ob.Append("\n\t\t}");
                    ob.Append("\n\t\tShading: T");
                    ob.Append("\n\t\tCulling: \"CullingOff\"\n\t}");

                    //connect Model to parent
                    GameObject parentObject = (GameObject) m_GameObject.Parent;
                    if (GameObjects.Contains(parentObject))
                    {
                        cb.AppendFormat("\n\n\t;Model::{0}, Model::{1}", m_GameObject.m_Name, parentObject.m_Name);
                        cb.AppendFormat("\n\tC: \"OO\",1{0},1{1}", m_GameObject.uniqueID, parentObject.uniqueID);
                    }
                    else
                    {
                        cb.AppendFormat("\n\n\t;Model::{0}, Model::RootNode", m_GameObject.m_Name);
                        cb.AppendFormat("\n\tC: \"OO\",1{0},0", m_GameObject.uniqueID);
                    }
                }

                #endregion

                #region write non-skinnned Geometry

                StatusStripUpdate("Writing Geometry");
                foreach (AssetPreloadData MeshPD in Meshes)
                {
                    Mesh m_Mesh = new Mesh(MeshPD);
                    MeshFBX(m_Mesh, MeshPD.uniqueID, ob);

                    //write data 8MB at a time
                    if (ob.Length > 8 * 0x100000)
                    {
                        FBXwriter.Write(ob);
                        ob.Clear();
                    }
                }

                #endregion

                #region write Deformer objects and skinned Geometry

                StringBuilder pb = new StringBuilder();
                //generate unique ID for BindPose
                pb.Append("\n\tPose: 5123456789, \"Pose::BIND_POSES\", \"BindPose\" {");
                pb.Append("\n\t\tType: \"BindPose\"");
                pb.Append("\n\t\tVersion: 100");
                pb.AppendFormat("\n\t\tNbPoseNodes: {0}", Skins.Count + LimbNodes.Count);

                foreach (AssetPreloadData SkinnedMeshPD in Skins)
                {
                    SkinnedMeshRenderer m_SkinnedMeshRenderer = new SkinnedMeshRenderer(SkinnedMeshPD);

                    GameObject m_GameObject;
                    AssetPreloadData MeshPD;
                    if (!assetsfileList.TryGetGameObject(m_SkinnedMeshRenderer.m_GameObject, out m_GameObject) ||
                        !assetsfileList.TryGetPD(m_SkinnedMeshRenderer.m_Mesh, out MeshPD))
                        continue;
                    // generate unique Geometry ID for instanced mesh objects
                    string keepID = MeshPD.uniqueID;
                    MeshPD.uniqueID = SkinnedMeshPD.uniqueID;
                    Mesh m_Mesh = new Mesh(MeshPD);
                    MeshFBX(m_Mesh, MeshPD.uniqueID, ob);

                    //write data 8MB at a time
                    if (ob.Length > 8 * 0x100000)
                    {
                        FBXwriter.Write(ob);
                        ob.Clear();
                    }

                    cb2.AppendFormat("\n\n\t;Geometry::, Model::{0}", m_GameObject.m_Name);
                    cb2.AppendFormat("\n\tC: \"OO\",3{0},1{1}", MeshPD.uniqueID, m_GameObject.uniqueID);

                    if (!(bool) Properties.Settings.Default["exportDeformers"])
                    {
                        MeshPD.uniqueID = keepID;
                        continue;
                    }

                    //add BindPose node
                    pb.Append("\n\t\tPoseNode:  {");
                    pb.AppendFormat("\n\t\t\tNode: 1{0}", m_GameObject.uniqueID);
                    //pb.Append("\n\t\t\tMatrix: *16 {");
                    //pb.Append("\n\t\t\t\ta: ");
                    //pb.Append("\n\t\t\t} ");
                    pb.Append("\n\t\t}");

                    ob.AppendFormat("\n\tCollectionExclusive: 5{0}, \"DisplayLayer::{1}\", \"DisplayLayer\" {{",
                        SkinnedMeshPD.uniqueID, m_GameObject.m_Name);
                    ob.Append("\n\t\tProperties70:  {");
                    ob.Append("\n\t\t}");
                    ob.Append("\n\t}");

                    //connect Model to DisplayLayer
                    cb2.AppendFormat("\n\n\t;Model::{0}, DisplayLayer::", m_GameObject.m_Name);
                    cb2.AppendFormat("\n\tC: \"OO\",1{0},5{1}", m_GameObject.uniqueID, SkinnedMeshPD.uniqueID);

                    //write Deformers
                    if (m_Mesh.m_Skin.Length <= 0 || m_Mesh.m_BindPose.Length < m_SkinnedMeshRenderer.m_Bones.Length)
                        continue;

                    //write main Skin Deformer
                    ob.AppendFormat("\n\tDeformer: 4{0}, \"Deformer::\", \"Skin\" {{", SkinnedMeshPD.uniqueID);
                    ob.Append("\n\t\tVersion: 101");
                    ob.Append("\n\t\tLink_DeformAcuracy: 50");
                    ob.Append("\n\t}"); //Deformer end

                    //connect Skin Deformer to Geometry
                    cb2.Append("\n\n\t;Deformer::, Geometry::");
                    cb2.AppendFormat("\n\tC: \"OO\",4{0},3{1}", SkinnedMeshPD.uniqueID, MeshPD.uniqueID);

                    for (int b = 0; b < m_SkinnedMeshRenderer.m_Bones.Length; b++)
                    {
                        Transform m_Transform;
                        if (!assetsfileList.TryGetTransform(m_SkinnedMeshRenderer.m_Bones[b], out m_Transform))
                            continue;

                        GameObject m_Bone;
                        if (!assetsfileList.TryGetGameObject(m_Transform.m_GameObject, out m_Bone))
                            continue;

                        int influences = 0, ibSplit = 0, wbSplit = 0;
                        StringBuilder ib = new StringBuilder(); //indices (vertex)
                        StringBuilder wb = new StringBuilder(); //weights

                        for (int index = 0; index < m_Mesh.m_Skin.Length; index++)
                        {
                            //if all weights (and indicces) are 0, bone0 has full control
                            if (Math.Abs(m_Mesh.m_Skin[index][0].weight) < 1e-4 &&
                                m_Mesh.m_Skin[index].All(x => Math.Abs(x.weight) < 1e-4) ||
                                m_Mesh.m_Skin[index][1].weight > 0)
                            {
                                // this implies a second bone exists, so bone0 has control too
                                // (otherwise it wouldn't be the first in the series)
                                m_Mesh.m_Skin[index][0].weight = 1;
                            }

                            Mesh.BoneInfluence influence = m_Mesh.m_Skin[index]
                                .Find(x => x.boneIndex == b && x.weight > 0);

                            if (influence == null)
                                continue;

                            influences++;
                            ib.AppendFormat("{0},", index);
                            wb.AppendFormat("{0},", influence.weight);

                            if (ib.Length - ibSplit > 2000)
                            {
                                ib.Append("\n");
                                ibSplit = ib.Length;
                            }
                            if (wb.Length - wbSplit > 2000)
                            {
                                wb.Append("\n");
                                wbSplit = wb.Length;
                            }
                        }

                        if (influences > 0)
                        {
                            ib.Length--; //remove last comma
                            wb.Length--; //remove last comma
                        }

                        //SubDeformer objects need unique IDs because 2 or more deformers can be linked to the same bone
                        ob.AppendFormat("\n\tDeformer: 4{0}{1}, \"SubDeformer::\", \"Cluster\" {{",
                            b, SkinnedMeshPD.uniqueID);
                        ob.Append("\n\t\tVersion: 100");
                        ob.Append("\n\t\tUserData: \"\", \"\"");

                        ob.AppendFormat("\n\t\tIndexes: *{0} {{\n\t\t\ta: ", influences);
                        ob.Append(ib);
                        ob.Append("\n\t\t}");
                        ib.Clear();

                        ob.AppendFormat("\n\t\tWeights: *{0} {{\n\t\t\ta: ", influences);
                        ob.Append(wb);
                        ob.Append("\n\t\t}");
                        wb.Clear();

                        ob.Append("\n\t\tTransform: *16 {\n\t\t\ta: ");

                        float[,] m = m_Mesh.m_BindPose[b];
                        ob.AppendFormat("{0},{1},{2},{3},", m[0, 0], -m[1, 0], -m[2, 0], m[3, 0]);
                        ob.AppendFormat("{0},{1},{2},{3},", -m[0, 1], m[1, 1], m[2, 1], m[3, 1]);
                        ob.AppendFormat("{0},{1},{2},{3},", -m[0, 2], m[1, 2], m[2, 2], m[3, 2]);
                        ob.AppendFormat("{0},{1},{2},{3},", -m[0, 3], m[1, 3], m[2, 3], m[3, 3]);
                        ob.Append("\n\t\t}");

                        ob.Append("\n\t}"); //SubDeformer end

                        //connect SubDeformer to Skin Deformer
                        cb2.Append("\n\n\t;SubDeformer::, Deformer::");
                        cb2.AppendFormat("\n\tC: \"OO\",4{0}{1},4{1}", b, SkinnedMeshPD.uniqueID);

                        //connect dummy Model to SubDeformer
                        cb2.AppendFormat("\n\n\t;Model::{0}, SubDeformer::", m_Bone.m_Name);
                        cb2.AppendFormat("\n\tC: \"OO\",1{0},4{1}{2}", m_Bone.uniqueID, b, SkinnedMeshPD.uniqueID);
                    }
                }

                if ((bool) Properties.Settings.Default["exportDeformers"])
                {
                    foreach (GameObject m_Bone in LimbNodes)
                    {
                        //add BindPose node
                        pb.Append("\n\t\tPoseNode:  {");
                        pb.AppendFormat("\n\t\t\tNode: 1{0}", m_Bone.uniqueID);
                        pb.Append("\n\t\t}");
                    }
                    pb.Append("\n\t}"); //BindPose end
                    ob.Append(pb);
                    pb.Clear();
                }

                #endregion

                ob.Append(mb);
                mb.Clear();
                cb.Append(cb2);
                cb2.Clear();

                #region write & extract Textures

                Directory.CreateDirectory(Path.GetDirectoryName(FBXfile) + "\\Texture2D");

                foreach (AssetPreloadData TexturePD in Textures)
                {
                    Texture2D m_Texture2D = new Texture2D(TexturePD, true);

                    //TODO check texture type and set path accordingly; eg. CubeMap, Texture3D
                    string texFilename = Path.GetDirectoryName(FBXfile) + "\\Texture2D\\" + TexturePD.Text;
                    if (uniqueNames.Checked)
                    {
                        texFilename += " #" + TexturePD.uniqueID;
                    }
                    texFilename += TexturePD.extension;

                    if (File.Exists(texFilename))
                    {
                        StatusStripUpdate("Texture file " + Path.GetFileName(texFilename) + " already exists");
                    }
                    else
                    {
                        StatusStripUpdate("Exporting Texture2D: " + Path.GetFileName(texFilename));
                        ExportTexture(m_Texture2D, texFilename);
                    }

                    ob.AppendFormat("\n\tTexture: 7{0}, \"Texture::{1}\", \"\" {{", TexturePD.uniqueID, TexturePD.Text);
                    ob.Append("\n\t\tType: \"TextureVideoClip\"");
                    ob.Append("\n\t\tVersion: 202");
                    ob.AppendFormat("\n\t\tTextureName: \"Texture::{0}\"", TexturePD.Text);
                    ob.Append("\n\t\tProperties70:  {");
                    ob.Append("\n\t\t\tP: \"UVSet\", \"KString\", \"\", \"\", \"UVChannel_0\"");
                    ob.Append("\n\t\t\tP: \"UseMaterial\", \"bool\", \"\", \"\",1");
                    ob.Append("\n\t\t}");
                    ob.AppendFormat("\n\t\tMedia: \"Video::{0}\"", TexturePD.Text);
                    ob.AppendFormat("\n\t\tFileName: \"{0}\"", texFilename);
                    ob.AppendFormat("\n\t\tRelativeFilename: \"Texture2D\\{0}\"", Path.GetFileName(texFilename));
                    ob.Append("\n\t}");

                    ob.AppendFormat("\n\tVideo: 8{0}, \"Video::{1}\", \"Clip\" {{", TexturePD.uniqueID, TexturePD.Text);
                    ob.Append("\n\t\tType: \"Clip\"");
                    ob.Append("\n\t\tProperties70:  {");
                    ob.AppendFormat("\n\t\t\tP: \"Path\", \"KString\", \"XRefUrl\", \"\", \"{0}\"", texFilename);
                    ob.Append("\n\t\t}");
                    ob.AppendFormat("\n\t\tFileName: \"{0}\"", texFilename);
                    ob.AppendFormat("\n\t\tRelativeFilename: \"Texture2D\\{0}\"", Path.GetFileName(texFilename));
                    ob.Append("\n\t}");

                    //connect video to texture
                    cb.AppendFormat("\n\n\t;Video::{0}, Texture::{0}", TexturePD.Text);
                    cb.AppendFormat("\n\tC: \"OO\",8{0},7{1}", TexturePD.uniqueID, TexturePD.uniqueID);
                }

                #endregion

                FBXwriter.Write(ob);
                ob.Clear();

                cb.Append("\n}"); //Connections end
                FBXwriter.Write(cb);
                cb.Clear();

                StatusStripUpdate("Finished exporting " + Path.GetFileName(FBXfile));
            }
        }

        private void MeshFBX(Mesh m_Mesh, string MeshID, StringBuilder ob)
        {
            if (m_Mesh.m_VertexCount > 0) //general failsafe
            {
                StatusStripUpdate("Writing Geometry: " + m_Mesh.m_Name);

                ob.AppendFormat("\n\tGeometry: 3{0}, \"Geometry::\", \"Mesh\" {{", MeshID);
                ob.Append("\n\t\tProperties70:  {");
                byte[] randomColor = RandomColorGenerator(m_Mesh.m_Name);
                ob.AppendFormat("\n\t\t\tP: \"Color\", \"ColorRGB\", \"Color\", \"\",{0},{1},{2}",
                    (float) randomColor[0] / 255, (float) randomColor[1] / 255, (float) randomColor[2] / 255);
                ob.Append("\n\t\t}");

                #region Vertices

                ob.AppendFormat("\n\t\tVertices: *{0} {{\n\t\t\ta: ", m_Mesh.m_VertexCount * 3);

                int c = 3; //vertex components
                //skip last component in vector4
                if (m_Mesh.m_Vertices.Length == m_Mesh.m_VertexCount * 4)
                {
                    c++;
                } //haha

                int lineSplit = ob.Length;
                for (int v = 0; v < m_Mesh.m_VertexCount; v++)
                {
                    ob.AppendFormat("{0},{1},{2},", -m_Mesh.m_Vertices[v * c], m_Mesh.m_Vertices[v * c + 1],
                        m_Mesh.m_Vertices[v * c + 2]);

                    if (ob.Length - lineSplit > 2000)
                    {
                        ob.Append("\n");
                        lineSplit = ob.Length;
                    }
                }
                ob.Length--; //remove last comma
                ob.Append("\n\t\t}");

                #endregion

                #region Indices

                // in order to test topology for triangles/quads we need to
                // store submeshes and write each one as geometry, then link to Mesh Node
                ob.AppendFormat("\n\t\tPolygonVertexIndex: *{0} {{\n\t\t\ta: ", m_Mesh.m_Indices.Count);

                lineSplit = ob.Length;
                for (int f = 0; f < m_Mesh.m_Indices.Count / 3; f++)
                {
                    ob.AppendFormat("{0},{1},{2},",
                        m_Mesh.m_Indices[f * 3],
                        m_Mesh.m_Indices[f * 3 + 2],
                        -m_Mesh.m_Indices[f * 3 + 1] - 1);

                    if (ob.Length - lineSplit > 2000)
                    {
                        ob.Append("\n");
                        lineSplit = ob.Length;
                    }
                }
                ob.Length--; //remove last comma

                ob.Append("\n\t\t}");
                ob.Append("\n\t\tGeometryVersion: 124");

                #endregion

                #region Normals

                if ((bool) Properties.Settings.Default["exportNormals"] && m_Mesh.m_Normals != null &&
                    m_Mesh.m_Normals.Length > 0)
                {
                    ob.Append("\n\t\tLayerElementNormal: 0 {");
                    ob.Append("\n\t\t\tVersion: 101");
                    ob.Append("\n\t\t\tName: \"\"");
                    ob.Append("\n\t\t\tMappingInformationType: \"ByVertice\"");
                    ob.Append("\n\t\t\tReferenceInformationType: \"Direct\"");
                    ob.AppendFormat("\n\t\t\tNormals: *{0} {{\n\t\t\ta: ", m_Mesh.m_VertexCount * 3);

                    if (m_Mesh.m_Normals.Length == m_Mesh.m_VertexCount * 3)
                    {
                        c = 3;
                    }
                    else if (m_Mesh.m_Normals.Length == m_Mesh.m_VertexCount * 4)
                    {
                        c = 4;
                    }

                    lineSplit = ob.Length;
                    for (int v = 0; v < m_Mesh.m_VertexCount; v++)
                    {
                        ob.AppendFormat("{0},{1},{2},", -m_Mesh.m_Normals[v * c], m_Mesh.m_Normals[v * c + 1],
                            m_Mesh.m_Normals[v * c + 2]);

                        if (ob.Length - lineSplit > 2000)
                        {
                            ob.Append("\n");
                            lineSplit = ob.Length;
                        }
                    }
                    ob.Length--; //remove last comma
                    ob.Append("\n\t\t\t}\n\t\t}");
                }

                #endregion

                #region Tangents

                if ((bool) Properties.Settings.Default["exportTangents"] && m_Mesh.m_Tangents != null &&
                    m_Mesh.m_Tangents.Length > 0)
                {
                    ob.Append("\n\t\tLayerElementTangent: 0 {");
                    ob.Append("\n\t\t\tVersion: 101");
                    ob.Append("\n\t\t\tName: \"\"");
                    ob.Append("\n\t\t\tMappingInformationType: \"ByVertice\"");
                    ob.Append("\n\t\t\tReferenceInformationType: \"Direct\"");
                    ob.AppendFormat("\n\t\t\tTangents: *{0} {{\n\t\t\ta: ", m_Mesh.m_VertexCount * 3);

                    if (m_Mesh.m_Tangents.Length == m_Mesh.m_VertexCount * 3)
                    {
                        c = 3;
                    }
                    else if (m_Mesh.m_Tangents.Length == m_Mesh.m_VertexCount * 4)
                    {
                        c = 4;
                    }

                    lineSplit = ob.Length;
                    for (int v = 0; v < m_Mesh.m_VertexCount; v++)
                    {
                        ob.AppendFormat("{0},{1},{2},", -m_Mesh.m_Tangents[v * c], m_Mesh.m_Tangents[v * c + 1],
                            m_Mesh.m_Tangents[v * c + 2]);

                        if (ob.Length - lineSplit > 2000)
                        {
                            ob.Append("\n");
                            lineSplit = ob.Length;
                        }
                    }
                    ob.Length--; //remove last comma
                    ob.Append("\n\t\t\t}\n\t\t}");
                }

                #endregion

                #region Colors

                int channelCount = -1;
                if ((bool) Properties.Settings.Default["exportColors"] && m_Mesh.m_Colors != null)
                {
                    if (m_Mesh.m_Colors.Length == m_Mesh.m_VertexCount * 4)
                        channelCount = 4;
                    else if (m_Mesh.m_Colors.Length == m_Mesh.m_VertexCount * 3)
                        channelCount = 3;
                    else if (m_Mesh.m_Colors.Length == m_Mesh.m_VertexCount * 2)
                        channelCount = 2;
                    else if (m_Mesh.m_Colors.Length == m_Mesh.m_VertexCount)
                        channelCount = 1;
                    else
                        channelCount = 0;
                }

                if (channelCount > 0)
                {
                    ob.Append("\n\t\tLayerElementColor: 0 {");
                    ob.Append("\n\t\t\tVersion: 101");
                    ob.Append("\n\t\t\tName: \"\"");
                    //ob.Append("\n\t\t\tMappingInformationType: \"ByVertice\"");
                    //ob.Append("\n\t\t\tReferenceInformationType: \"Direct\"");
                    ob.Append("\n\t\t\tMappingInformationType: \"ByPolygonVertex\"");
                    ob.Append("\n\t\t\tReferenceInformationType: \"IndexToDirect\"");
                    ob.AppendFormat("\n\t\t\tColors: *{0} {{\n\t\t\ta: ", m_Mesh.m_Colors.Length);
                    //ob.Append(string.Join(",", m_Mesh.m_Colors));

                    lineSplit = ob.Length;
                    for (int i = 0; i < m_Mesh.m_VertexCount; i++)
                    {
                        int offset = i * channelCount;
                        switch (channelCount)
                        {
                            case 4:
                                ob.AppendFormat("{0},{1},{2},{3},",
                                    m_Mesh.m_Colors[offset + 0], m_Mesh.m_Colors[offset + 1],
                                    m_Mesh.m_Colors[offset + 2], m_Mesh.m_Colors[offset + 3]);
                                break;
                            case 3:
                                ob.AppendFormat("{0},{1},{2},{3},",
                                    m_Mesh.m_Colors[offset + 0], m_Mesh.m_Colors[offset + 1],
                                    m_Mesh.m_Colors[offset + 2], 1.0f);
                                break;
                            case 2:
                                ob.AppendFormat("{0},{1},{2},{3},",
                                    m_Mesh.m_Colors[offset + 0], m_Mesh.m_Colors[offset + 1], 0.0f, 1.0f);
                                break;
                            case 1:
                                ob.AppendFormat("{0},{1},{2},{3},",
                                    m_Mesh.m_Colors[offset], m_Mesh.m_Colors[offset], m_Mesh.m_Colors[offset], 1.0f);
                                break;
                        }

                        if (ob.Length - lineSplit > 2000)
                        {
                            ob.Append("\n");
                            lineSplit = ob.Length;
                        }
                    }
                    ob.Length--; //remove last comma

                    ob.Append("\n\t\t\t}");
                    ob.AppendFormat("\n\t\t\tColorIndex: *{0} {{\n\t\t\ta: ", m_Mesh.m_Indices.Count);

                    lineSplit = ob.Length;
                    for (int f = 0; f < m_Mesh.m_Indices.Count / 3; f++)
                    {
                        ob.AppendFormat("{0},{1},{2},", m_Mesh.m_Indices[f * 3], m_Mesh.m_Indices[f * 3 + 2],
                            m_Mesh.m_Indices[f * 3 + 1]);

                        if (ob.Length - lineSplit > 2000)
                        {
                            ob.Append("\n");
                            lineSplit = ob.Length;
                        }
                    }
                    ob.Length--; //remove last comma

                    ob.Append("\n\t\t\t}\n\t\t}");
                }

                #endregion

                #region UV1

                //does FBX support UVW coordinates?
                if ((bool) Properties.Settings.Default["exportUVs"] && m_Mesh.m_UV1 != null && m_Mesh.m_UV1.Length > 0)
                {
                    ob.Append("\n\t\tLayerElementUV: 0 {");
                    ob.Append("\n\t\t\tVersion: 101");
                    ob.Append("\n\t\t\tName: \"UVChannel_1\"");
                    ob.Append("\n\t\t\tMappingInformationType: \"ByVertice\"");
                    ob.Append("\n\t\t\tReferenceInformationType: \"Direct\"");
                    ob.AppendFormat("\n\t\t\tUV: *{0} {{\n\t\t\ta: ", m_Mesh.m_UV1.Length);

                    lineSplit = ob.Length;
                    for (int v = 0; v < m_Mesh.m_VertexCount; v++)
                    {
                        ob.AppendFormat("{0},{1},", m_Mesh.m_UV1[v * 2], 1 - m_Mesh.m_UV1[v * 2 + 1]);

                        if (ob.Length - lineSplit > 2000)
                        {
                            ob.Append("\n");
                            lineSplit = ob.Length;
                        }
                    }
                    ob.Length--; //remove last comma
                    ob.Append("\n\t\t\t}\n\t\t}");
                }

                #endregion

                #region UV2

                if ((bool) Properties.Settings.Default["exportUVs"] && m_Mesh.m_UV2 != null && m_Mesh.m_UV2.Length > 0)
                {
                    ob.Append("\n\t\tLayerElementUV: 1 {");
                    ob.Append("\n\t\t\tVersion: 101");
                    ob.Append("\n\t\t\tName: \"UVChannel_2\"");
                    ob.Append("\n\t\t\tMappingInformationType: \"ByVertice\"");
                    ob.Append("\n\t\t\tReferenceInformationType: \"Direct\"");
                    ob.AppendFormat("\n\t\t\tUV: *{0} {{\n\t\t\ta: ", m_Mesh.m_UV2.Length);

                    lineSplit = ob.Length;
                    for (int v = 0; v < m_Mesh.m_VertexCount; v++)
                    {
                        ob.AppendFormat("{0},{1},", m_Mesh.m_UV2[v * 2], 1 - m_Mesh.m_UV2[v * 2 + 1]);

                        if (ob.Length - lineSplit > 2000)
                        {
                            ob.Append("\n");
                            lineSplit = ob.Length;
                        }
                    }
                    ob.Length--; //remove last comma
                    ob.Append("\n\t\t\t}\n\t\t}");
                }

                #endregion

                #region UV3

                if ((bool) Properties.Settings.Default["exportUVs"] && m_Mesh.m_UV3 != null && m_Mesh.m_UV3.Length > 0)
                {
                    ob.Append("\n\t\tLayerElementUV: 2 {");
                    ob.Append("\n\t\t\tVersion: 101");
                    ob.Append("\n\t\t\tName: \"UVChannel_3\"");
                    ob.Append("\n\t\t\tMappingInformationType: \"ByVertice\"");
                    ob.Append("\n\t\t\tReferenceInformationType: \"Direct\"");
                    ob.AppendFormat("\n\t\t\tUV: *{0} {{\n\t\t\ta: ", m_Mesh.m_UV3.Length);

                    lineSplit = ob.Length;
                    for (int v = 0; v < m_Mesh.m_VertexCount; v++)
                    {
                        ob.AppendFormat("{0},{1},", m_Mesh.m_UV3[v * 2], 1 - m_Mesh.m_UV3[v * 2 + 1]);

                        if (ob.Length - lineSplit > 2000)
                        {
                            ob.Append("\n");
                            lineSplit = ob.Length;
                        }
                    }
                    ob.Length--; //remove last comma
                    ob.Append("\n\t\t\t}\n\t\t}");
                }

                #endregion

                #region UV4

                if ((bool) Properties.Settings.Default["exportUVs"] && m_Mesh.m_UV4 != null && m_Mesh.m_UV4.Length > 0)
                {
                    ob.Append("\n\t\tLayerElementUV: 3 {");
                    ob.Append("\n\t\t\tVersion: 101");
                    ob.Append("\n\t\t\tName: \"UVChannel_4\"");
                    ob.Append("\n\t\t\tMappingInformationType: \"ByVertice\"");
                    ob.Append("\n\t\t\tReferenceInformationType: \"Direct\"");
                    ob.AppendFormat("\n\t\t\tUV: *{0} {{\n\t\t\ta: ", m_Mesh.m_UV4.Length);

                    lineSplit = ob.Length;
                    for (int v = 0; v < m_Mesh.m_VertexCount; v++)
                    {
                        ob.AppendFormat("{0},{1},", m_Mesh.m_UV4[v * 2], 1 - m_Mesh.m_UV4[v * 2 + 1]);

                        if (ob.Length - lineSplit > 2000)
                        {
                            ob.Append("\n");
                            lineSplit = ob.Length;
                        }
                    }
                    ob.Length--; //remove last comma
                    ob.Append("\n\t\t\t}\n\t\t}");
                }

                #endregion

                #region Material

                ob.Append("\n\t\tLayerElementMaterial: 0 {");
                ob.Append("\n\t\t\tVersion: 101");
                ob.Append("\n\t\t\tName: \"\"");
                ob.Append("\n\t\t\tMappingInformationType: \"");
                if (m_Mesh.m_SubMeshes.Count == 1)
                {
                    ob.Append("AllSame\"");
                }
                else
                {
                    ob.Append("ByPolygon\"");
                }
                ob.Append("\n\t\t\tReferenceInformationType: \"IndexToDirect\"");
                ob.AppendFormat("\n\t\t\tMaterials: *{0} {{", m_Mesh.m_materialIDs.Count);
                ob.Append("\n\t\t\t\t");
                if (m_Mesh.m_SubMeshes.Count == 1)
                {
                    ob.Append("0");
                }
                else
                {
                    lineSplit = ob.Length;
                    foreach (int materialId in m_Mesh.m_materialIDs)
                    {
                        ob.AppendFormat("{0},", materialId);

                        if (ob.Length - lineSplit > 2000)
                        {
                            ob.Append("\n");
                            lineSplit = ob.Length;
                        }
                    }
                    ob.Length--; //remove last comma
                }
                ob.Append("\n\t\t\t}\n\t\t}");

                #endregion

                #region Layers

                ob.Append("\n\t\tLayer: 0 {");
                ob.Append("\n\t\t\tVersion: 100");

                if ((bool) Properties.Settings.Default["exportNormals"] && m_Mesh.m_Normals != null &&
                    m_Mesh.m_Normals.Length > 0)
                {
                    ob.Append("\n\t\t\tLayerElement:  {");
                    ob.Append("\n\t\t\t\tType: \"LayerElementNormal\"");
                    ob.Append("\n\t\t\t\tTypedIndex: 0");
                    ob.Append("\n\t\t\t}");
                }

                if ((bool) Properties.Settings.Default["exportTangents"] && m_Mesh.m_Tangents != null &&
                    m_Mesh.m_Tangents.Length > 0)
                {
                    ob.Append("\n\t\t\tLayerElement:  {");
                    ob.Append("\n\t\t\t\tType: \"LayerElementTangent\"");
                    ob.Append("\n\t\t\t\tTypedIndex: 0");
                    ob.Append("\n\t\t\t}");
                }

                ob.Append("\n\t\t\tLayerElement:  {");
                ob.Append("\n\t\t\t\tType: \"LayerElementMaterial\"");
                ob.Append("\n\t\t\t\tTypedIndex: 0");
                ob.Append("\n\t\t\t}");

                if ((bool) Properties.Settings.Default["exportColors"] && m_Mesh.m_Colors != null &&
                    m_Mesh.m_Colors.Length > 0)
                {
                    ob.Append("\n\t\t\tLayerElement:  {");
                    ob.Append("\n\t\t\t\tType: \"LayerElementColor\"");
                    ob.Append("\n\t\t\t\tTypedIndex: 0");
                    ob.Append("\n\t\t\t}");
                }

                if ((bool) Properties.Settings.Default["exportUVs"] && m_Mesh.m_UV1 != null && m_Mesh.m_UV1.Length > 0)
                {
                    ob.Append("\n\t\t\tLayerElement:  {");
                    ob.Append("\n\t\t\t\tType: \"LayerElementUV\"");
                    ob.Append("\n\t\t\t\tTypedIndex: 0");
                    ob.Append("\n\t\t\t}");
                }
                ob.Append("\n\t\t}"); //Layer 0 end

                if ((bool) Properties.Settings.Default["exportUVs"] && m_Mesh.m_UV2 != null && m_Mesh.m_UV2.Length > 0)
                {
                    ob.Append("\n\t\tLayer: 1 {");
                    ob.Append("\n\t\t\tVersion: 100");
                    ob.Append("\n\t\t\tLayerElement:  {");
                    ob.Append("\n\t\t\t\tType: \"LayerElementUV\"");
                    ob.Append("\n\t\t\t\tTypedIndex: 1");
                    ob.Append("\n\t\t\t}");
                    ob.Append("\n\t\t}"); //Layer 1 end
                }

                if ((bool) Properties.Settings.Default["exportUVs"] && m_Mesh.m_UV3 != null && m_Mesh.m_UV3.Length > 0)
                {
                    ob.Append("\n\t\tLayer: 2 {");
                    ob.Append("\n\t\t\tVersion: 100");
                    ob.Append("\n\t\t\tLayerElement:  {");
                    ob.Append("\n\t\t\t\tType: \"LayerElementUV\"");
                    ob.Append("\n\t\t\t\tTypedIndex: 2");
                    ob.Append("\n\t\t\t}");
                    ob.Append("\n\t\t}"); //Layer 2 end
                }

                if ((bool) Properties.Settings.Default["exportUVs"] && m_Mesh.m_UV4 != null && m_Mesh.m_UV4.Length > 0)
                {
                    ob.Append("\n\t\tLayer: 3 {");
                    ob.Append("\n\t\t\tVersion: 100");
                    ob.Append("\n\t\t\tLayerElement:  {");
                    ob.Append("\n\t\t\t\tType: \"LayerElementUV\"");
                    ob.Append("\n\t\t\t\tTypedIndex: 3");
                    ob.Append("\n\t\t\t}");
                    ob.Append("\n\t\t}"); //Layer 3 end
                }

                #endregion

                ob.Append("\n\t}"); //Geometry end
            }
        }
    }
}
