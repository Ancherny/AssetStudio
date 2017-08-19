
namespace UnityStudio
{
    internal class Material
    {
        public readonly string m_Name;
        public readonly TexEnv[] m_TexEnvs;
        public readonly StrFloatPair[] m_Floats;
        public readonly StrColorPair[] m_Colors;

        public Material(AssetPreloadData preloadData)
        {
            AssetsFile sourceFile = preloadData.sourceFile;
            EndianStream a_Stream = preloadData.sourceFile.a_Stream;
            a_Stream.Position = preloadData.Offset;

            if (sourceFile.platform == -2)
            {
                a_Stream.ReadUInt32(); // uint m_ObjectHideFlags
                sourceFile.ReadPPtr(); // PPtr m_PrefabParentObject
                sourceFile.ReadPPtr(); // PPtr m_PrefabInternal
            }

            m_Name = Helpers.FixMayaName(a_Stream.ReadAlignedString(a_Stream.ReadInt32()));
            sourceFile.ReadPPtr(); // PPtr m_Shader

            if (sourceFile.version[0] == 4 && (sourceFile.version[1] >= 2 || sourceFile.version[1] == 1 && sourceFile.buildType[0] != "a"))
            {
                string[] shaderKeywords = new string[a_Stream.ReadInt32()];
                for (int i = 0; i < shaderKeywords.Length; i++)
                {
                    shaderKeywords[i] = a_Stream.ReadAlignedString(a_Stream.ReadInt32());
                }
            }
            else if (sourceFile.version[0] == 5)
            {
                a_Stream.ReadAlignedString(a_Stream.ReadInt32()); // string[] shaderKeywords
                a_Stream.ReadUInt32(); // uint m_LightmapFlags
            }

            if (sourceFile.version[0] > 4 || sourceFile.version[0] == 4 && sourceFile.version[1] >= 3)
            {
                a_Stream.ReadInt32(); // int m_CustomRenderQueue
            }

            if (sourceFile.version[0] == 5 && sourceFile.version[1] >= 1)
            {
                string[][] stringTagMap = new string[a_Stream.ReadInt32()][];
                for (int i = 0; i < stringTagMap.Length; i++)
                {
                    stringTagMap[i] = new []
                    {
                        a_Stream.ReadAlignedString(a_Stream.ReadInt32()),
                        a_Stream.ReadAlignedString(a_Stream.ReadInt32())
                    };
                }
            }

            //m_SavedProperties
            m_TexEnvs = new TexEnv[a_Stream.ReadInt32()];
            for (int i = 0; i < m_TexEnvs.Length; i++)
            {
                TexEnv m_TexEnv = new TexEnv()
                {
                    name = a_Stream.ReadAlignedString(a_Stream.ReadInt32()),
                    m_Texture = sourceFile.ReadPPtr(),
                    m_Scale = new []
                    {
                        a_Stream.ReadSingle(),
                        a_Stream.ReadSingle()
                    },
                    m_Offset = new []
                    {
                        a_Stream.ReadSingle(),
                        a_Stream.ReadSingle()
                    }
                };
                m_TexEnvs[i] = m_TexEnv;

                // Eliminate 'unused' warning on class members
                float[] unused1 = m_TexEnv.m_Scale;
                float[] unused2 = m_TexEnv.m_Offset;
            }

            m_Floats = new StrFloatPair[a_Stream.ReadInt32()];
            for (int i = 0; i < m_Floats.Length; i++)
            {
                StrFloatPair m_Float = new StrFloatPair()
                {
                    first = a_Stream.ReadAlignedString(a_Stream.ReadInt32()),
                    second = a_Stream.ReadSingle()
                };
                m_Floats[i] = m_Float;
            }

            m_Colors = new StrColorPair[a_Stream.ReadInt32()];
            for (int i = 0; i < m_Colors.Length; i++)
            {
                StrColorPair m_Color = new StrColorPair()
                {
                    first = a_Stream.ReadAlignedString(a_Stream.ReadInt32()),
                    second = new []
                    {
                        a_Stream.ReadSingle(),
                        a_Stream.ReadSingle(),
                        a_Stream.ReadSingle(),
                        a_Stream.ReadSingle()
                    }
                };
                m_Colors[i] = m_Color;
            }
        }
    }
}
