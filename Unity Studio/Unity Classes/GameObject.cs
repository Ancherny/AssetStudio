using System.Windows.Forms;

namespace UnityStudio
{
    public class GameObject : TreeNode
    {
        public readonly PPtr m_Transform;
        public readonly PPtr m_Renderer;
        public readonly PPtr m_MeshFilter;
        public readonly PPtr m_SkinnedMeshRenderer;
        public string m_Name;

        public readonly string uniqueID = "0"; //this way file and folder TreeNodes will be treated as FBX scene

        public GameObject(AssetPreloadData preloadData)
        {
            if (preloadData != null)
            {
                AssetsFile sourceFile = preloadData.sourceFile;
                EndianStream a_Stream = preloadData.sourceFile.a_Stream;
                a_Stream.Position = preloadData.Offset;

                uniqueID = preloadData.uniqueID;

                if (sourceFile.platform == -2)
                {
                    a_Stream.ReadUInt32(); // uint m_ObjectHideFlags
                    sourceFile.ReadPPtr(); // PPtr m_PrefabParentObject
                    sourceFile.ReadPPtr(); // PPtr m_PrefabInternal
                }

                int m_Component_size = a_Stream.ReadInt32();
                for (int j = 0; j < m_Component_size; j++)
                {
                    int m_Component_type = a_Stream.ReadInt32();

                    switch (m_Component_type)
                    {
                        case 4:
                            m_Transform = sourceFile.ReadPPtr();
                            break;
                        case 23:
                            m_Renderer = sourceFile.ReadPPtr();
                            break;
                        case 33:
                            m_MeshFilter = sourceFile.ReadPPtr();
                            break;
                        case 137:
                            m_SkinnedMeshRenderer = sourceFile.ReadPPtr();
                            break;
                        default:
                            sourceFile.ReadPPtr(); // PPtr m_Component
                            break;
                    }
                }

                a_Stream.ReadInt32(); // int m_Layer
                int namesize = a_Stream.ReadInt32();
                m_Name = a_Stream.ReadAlignedString(namesize);
                if (m_Name == "")
                {
                    m_Name = "GameObject #" + uniqueID;
                }
                a_Stream.ReadUInt16(); // ushort m_Tag
                a_Stream.ReadBoolean(); // bool m_IsActive

                Text = m_Name;
                //name should be unique
                Name = uniqueID;
            }
        }
    }
}
