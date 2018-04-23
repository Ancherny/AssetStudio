using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Tao.DevIl;

namespace AssetStudio
{
    public partial class AssetStudioForm
    {
        [DllImport("gdi32.dll")]
        private static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, [In] ref uint pcFonts);

        private static Bitmap DDSDataToBMP(byte[] DDSData)
        {
            // Create a DevIL image "name" (which is actually a number)
            int img_name;
            Il.ilGenImages(1, out img_name);
            Il.ilBindImage(img_name);

            // Load the DDS file into the bound DevIL image
            Il.ilLoadL(Il.IL_DDS, DDSData, DDSData.Length);

            // Set a few size variables that will simplify later code

            int ImgWidth = Il.ilGetInteger(Il.IL_IMAGE_WIDTH);
            int ImgHeight = Il.ilGetInteger(Il.IL_IMAGE_HEIGHT);
            Rectangle rect = new Rectangle(0, 0, ImgWidth, ImgHeight);

            // Convert the DevIL image to a pixel byte array to copy into Bitmap
            Il.ilConvertImage(Il.IL_BGRA, Il.IL_UNSIGNED_BYTE);

            // Create a Bitmap to copy the image into, and prepare it to get data
            Bitmap bmp = new Bitmap(ImgWidth, ImgHeight);
            BitmapData bmd =
                bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            // Copy the pixel byte array from the DevIL image to the Bitmap
            Il.ilCopyPixels(0, 0, 0,
                Il.ilGetInteger(Il.IL_IMAGE_WIDTH),
                Il.ilGetInteger(Il.IL_IMAGE_HEIGHT),
                1, Il.IL_BGRA, Il.IL_UNSIGNED_BYTE,
                bmd.Scan0);

            // Clean up and return Bitmap
            Il.ilDeleteImages(1, ref img_name);
            bmp.UnlockBits(bmd);
            return bmp;
        }

        private static float[] QuatToEuler(float[] q)
        {
            double eax;
            double eay;
            double eaz;

            float qx = q[0];
            float qy = q[1];
            float qz = q[2];
            float qw = q[3];

            double[,] M = new double[4, 4];

            double Nq = qx * qx + qy * qy + qz * qz + qw * qw;
            double s = (Nq > 0.0) ? (2.0 / Nq) : 0.0;
            double xs = qx * s, ys = qy * s, zs = qz * s;
            double wx = qw * xs, wy = qw * ys, wz = qw * zs;
            double xx = qx * xs, xy = qx * ys, xz = qx * zs;
            double yy = qy * ys, yz = qy * zs, zz = qz * zs;

            M[0, 0] = 1.0 - (yy + zz); M[0, 1] = xy - wz; M[0, 2] = xz + wy;
            M[1, 0] = xy + wz; M[1, 1] = 1.0 - (xx + zz); M[1, 2] = yz - wx;
            M[2, 0] = xz - wy; M[2, 1] = yz + wx; M[2, 2] = 1.0 - (xx + yy);
            M[3, 0] = M[3, 1] = M[3, 2] = M[0, 3] = M[1, 3] = M[2, 3] = 0.0; M[3, 3] = 1.0;

            double test = Math.Sqrt(M[0, 0] * M[0, 0] + M[1, 0] * M[1, 0]);
            if (test > 16 * 1.19209290E-07F)//FLT_EPSILON
            {
                eax = Math.Atan2(M[2, 1], M[2, 2]);
                eay = Math.Atan2(-M[2, 0], test);
                eaz = Math.Atan2(M[1, 0], M[0, 0]);
            }
            else
            {
                eax = Math.Atan2(-M[1, 2], M[1, 1]);
                eay = Math.Atan2(-M[2, 0], test);
                eaz = 0;
            }

            return new []
            {
                (float)(eax * 180 / Math.PI),
                (float)(eay * 180 / Math.PI),
                (float)(eaz * 180 / Math.PI)
            };
        }

        private static byte[] RandomColorGenerator(string name)
        {
            int nameHash = name.GetHashCode();
            Random r = new Random(nameHash);
            //Random r = new Random(DateTime.Now.Millisecond);

            byte red = (byte)r.Next(0, 255);
            byte green = (byte)r.Next(0, 255);
            byte blue = (byte)r.Next(0, 255);

            return new []
            {
                red,
                green,
                blue
            };
        }

        private static void ExportTexture (Texture2D m_Texture2D, string exportFilename)
        {
            switch (m_Texture2D.m_TextureFormat)
            {
                #region DDS
                case 1: //Alpha8
                case 2: //A4R4G4B4
                case 3: //B8G8R8 //confirmed on X360, iOS //PS3 unsure
                case 4: //G8R8A8B8 //confirmed on X360, iOS
                case 5: //B8G8R8A8 //confirmed on X360, PS3, Web, iOS
                case 7: //R5G6B5 //confirmed switched on X360; confirmed on iOS
                case 10: //DXT1
                case 12: //DXT5
                case 13: //R4G4B4A4, iOS (only?)
                    using (BinaryWriter writer = new BinaryWriter(File.Open(exportFilename, FileMode.Create)))
                    {
                        writer.Write(0x20534444);
                        writer.Write(0x7C);
                        writer.Write(m_Texture2D.dwFlags);
                        writer.Write(m_Texture2D.m_Height);
                        writer.Write(m_Texture2D.m_Width);
                        writer.Write(m_Texture2D.dwPitchOrLinearSize); //should be main tex size without mips);
                        writer.Write(0); //dwDepth not implemented
                        writer.Write(m_Texture2D.dwMipMapCount);
                        writer.Write(new byte[44]); //dwReserved1[11]
                        writer.Write(m_Texture2D.dwSize);
                        writer.Write(m_Texture2D.dwFlags2);
                        writer.Write(m_Texture2D.dwFourCC);
                        writer.Write(m_Texture2D.dwRGBBitCount);
                        writer.Write(m_Texture2D.dwRBitMask);
                        writer.Write(m_Texture2D.dwGBitMask);
                        writer.Write(m_Texture2D.dwBBitMask);
                        writer.Write(m_Texture2D.dwABitMask);
                        writer.Write(m_Texture2D.dwCaps);
                        writer.Write(m_Texture2D.dwCaps2);
                        writer.Write(new byte[12]); //dwCaps3&4 & dwReserved2

                        writer.Write(m_Texture2D.image_data);
                        writer.Close();
                    }
                    break;
                #endregion
                #region PVR
                case 30: //PVRTC_RGB2
                case 31: //PVRTC_RGBA2
                case 32: //PVRTC_RGB4
                case 33: //PVRTC_RGBA4
                case 34: //ETC_RGB4
                    using (BinaryWriter writer = new BinaryWriter(File.Open(exportFilename, FileMode.Create)))
                    {
                        writer.Write(m_Texture2D.pvrVersion);
                        writer.Write(m_Texture2D.pvrFlags);
                        writer.Write(m_Texture2D.pvrPixelFormat);
                        writer.Write(m_Texture2D.pvrColourSpace);
                        writer.Write(m_Texture2D.pvrChannelType);
                        writer.Write(m_Texture2D.m_Height);
                        writer.Write(m_Texture2D.m_Width);
                        writer.Write(m_Texture2D.pvrDepth);
                        writer.Write(m_Texture2D.pvrNumSurfaces);
                        writer.Write(m_Texture2D.pvrNumFaces);
                        writer.Write(m_Texture2D.dwMipMapCount);
                        writer.Write(m_Texture2D.pvrMetaDataSize);

                        writer.Write(m_Texture2D.image_data);
                        writer.Close();
                    }
                    break;
                #endregion
//                case 28: //DXT1 Crunched
//                case 29: //DXT1 Crunched
                default:
                    using (BinaryWriter writer = new BinaryWriter(File.Open(exportFilename, FileMode.Create)))
                    {
                        writer.Write(m_Texture2D.image_data);
                        writer.Close();
                    }
                    break;
            }
        }

        private static void ExportAudioClip(AudioClip m_AudioClip, string exportFilename)
        {
            using (BinaryWriter writer = new BinaryWriter(File.Open(exportFilename, FileMode.Create)))
            {
                writer.Write(m_AudioClip.m_AudioData);
                writer.Close();
            }
        }

        private static void ExportText(TextAsset m_TextAsset, string exportFilename)
        {
            using (BinaryWriter writer = new BinaryWriter(File.Open(exportFilename, FileMode.Create)))
            {
                writer.Write(m_TextAsset.m_Script);
                writer.Close();
            }
        }

        private static void ExportFont(AssetFont m_Font, string exportFilename)
        {
            using (BinaryWriter writer = new BinaryWriter(File.Open(exportFilename, FileMode.Create)))
            {
                writer.Write(m_Font.m_FontData);
                writer.Close();
            }
        }
    }
}
