﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// TODO For extracting bundles, first check if file exists then decompress
// TODO Font index error in Dreamfall Chapters

namespace UnityStudio
{
    public partial class UnityStudioForm : Form
    {
        //files to load
        private List<string> unityFiles = new List<string>();

        //loaded files
        private static readonly List<AssetsFile> assetsfileList = new List<AssetsFile>();

        //used to hold all assets while the ListView is filtered
        private readonly List<AssetPreloadData> exportableAssets = new List<AssetPreloadData>();

        //used to build the ListView from all or filtered assets
        private List<AssetPreloadData> visibleAssets = new List<AssetPreloadData>();

        private AssetPreloadData lastSelectedItem;

        private AssetPreloadData lastLoadedAsset;

        //private AssetsFile mainDataFile;
        private string mainPath = "";

        private string productName = "";

        private readonly string[] fileTypes =
        {
            "maindata.",
            "level*.",
            "*.assets",
            "*.sharedAssets",
            "CustomAssetBundle-*",
            "CAB-*",
            "BuildPlayer-*"
        };

        private Dictionary<string, Dictionary<string, string>> jsonMats;

        private readonly Dictionary<string, SortedDictionary<int, ClassStrStruct>> AllClassStructures =
            new Dictionary<string, SortedDictionary<int, ClassStrStruct>>();

        private Bitmap imageTexture;

        //asset list sorting helpers
        private int firstSortColumn = -1;

        private int secondSortColumn;
        private bool reverseSort;
        private bool enableFiltering;

        //tree search
        private int nextGObject;

        private readonly List<GameObject> treeSrcResults = new List<GameObject>();

        //counters for progress bar
        private int totalAssetCount;

        private int totalTreeNodes;

        private System.Drawing.Text.PrivateFontCollection pfc = new System.Drawing.Text.PrivateFontCollection();

        private void loadFile_Click(object sender, EventArgs e)
        {
            if (openFileDialog1.ShowDialog() == DialogResult.OK)
            {
                resetForm();
                mainPath = Path.GetDirectoryName(openFileDialog1.FileNames[0]);

                if (openFileDialog1.FilterIndex == 1)
                {
                    MergeSplitAssets(mainPath);

                    unityFiles.AddRange(openFileDialog1.FileNames);
                    progressBar1.Value = 0;
                    progressBar1.Maximum = unityFiles.Count;

                    //use a for loop because list size can change
                    foreach (string unityFile in unityFiles)
                    {
                        StatusStripUpdate("Loading " + Path.GetFileName(unityFile));
                        LoadAssetsFile(unityFile);
                    }
                }
                else
                {
                    progressBar1.Value = 0;
                    progressBar1.Maximum = openFileDialog1.FileNames.Length;

                    foreach (string filename in openFileDialog1.FileNames)
                    {
                        LoadBundleFile(filename);
                        progressBar1.PerformStep();
                    }
                }

                progressBar1.Value = 0;

                BuildAssetStrucutres();
            }
        }

        private void loadFolder_Click(object sender, EventArgs e)
        {
            if (openFolderDialog1.ShowDialog() != DialogResult.OK)
            {
                StatusStripUpdate("Selected path deos not exist.");
                return;
            }

            //mainPath = folderBrowserDialog1.SelectedPath;
            mainPath = openFolderDialog1.FileName;
            if (Path.GetFileName(mainPath) == "Select folder")
            {
                mainPath = Path.GetDirectoryName(mainPath);
            }

            if (!Directory.Exists(mainPath))
                return;
            {
                resetForm();

                // TODO find a way to read data directly instead of merging files
                MergeSplitAssets(mainPath);

                for (int t = 0; t < fileTypes.Length; t++)
                {
                    string[] fileNames = Directory.GetFiles(mainPath, fileTypes[t], SearchOption.AllDirectories);

                    #region  sort specific types alphanumerically

                    if (fileNames.Length > 0 && (t == 1 || t == 2))
                    {
                        List<string> sortedList = fileNames.ToList();
                        sortedList.Sort((s1, s2) =>
                        {
                            const string pattern = "([A-Za-z\\s]*)([0-9]*)";

                            // ReSharper disable once AssignNullToNotNullAttribute
                            string h1 = Regex.Match(Path.GetFileNameWithoutExtension(s1), pattern).Groups[1].Value;
                            // ReSharper disable once AssignNullToNotNullAttribute
                            string h2 = Regex.Match(Path.GetFileNameWithoutExtension(s2), pattern).Groups[1].Value;
                            if (h1 != h2)
                                return string.Compare(h1, h2, StringComparison.InvariantCulture);

                            // ReSharper disable once AssignNullToNotNullAttribute
                            string t1 = Regex.Match(Path.GetFileNameWithoutExtension(s1), pattern).Groups[2].Value;
                            // ReSharper disable once AssignNullToNotNullAttribute
                            string t2 = Regex.Match(Path.GetFileNameWithoutExtension(s2), pattern).Groups[2].Value;
                            if (t1 != "" && t2 != "")
                                return int.Parse(t1).CompareTo(int.Parse(t2));

                            return 0;
                        });
                        unityFiles.AddRange(sortedList);
                    }

                    #endregion

                    else
                    {
                        unityFiles.AddRange(fileNames);
                    }
                }

                unityFiles = unityFiles.Distinct().ToList();
                progressBar1.Value = 0;
                progressBar1.Maximum = unityFiles.Count;

                //use a for loop because list size can change
                foreach (string unityFile in unityFiles)
                {
                    StatusStripUpdate("Loading " + Path.GetFileName(unityFile));
                    LoadAssetsFile(unityFile);
                }

                progressBar1.Value = 0;

                BuildAssetStrucutres();
            }
        }

        private void MergeSplitAssets(string dirPath)
        {
            string[] splitFiles = Directory.GetFiles(dirPath, "*.split0");
            foreach (string splitFile in splitFiles)
            {
                string destFile = Path.GetFileNameWithoutExtension(splitFile);
                string destPath = Path.GetDirectoryName(splitFile) + "\\";
                if (!File.Exists(destPath + destFile))
                {
                    StatusStripUpdate("Merging " + destFile + " split files...");

                    string[] splitParts = Directory.GetFiles(destPath, destFile + ".split*");
                    using (FileStream destStream = File.Create(destPath + destFile))
                    {
                        for (int i = 0; i < splitParts.Length; i++)
                        {
                            string splitPart = destPath + destFile + ".split" + i.ToString();
                            using (FileStream sourceStream = File.OpenRead(splitPart))
                                sourceStream.CopyTo(destStream); // You can pass the buffer size as second argument.
                        }
                    }
                }
            }
        }

        private void LoadAssetsFile(string fileName)
        {
            AssetsFile loadedAssetsFile = assetsfileList.Find(aFile => aFile.filePath == fileName);
            if (loadedAssetsFile == null)
            {
                //open file here and pass the stream to facilitate loading memory files
                //also by keeping the stream as a property of AssetsFile, it can be used later on to read assets
                AssetsFile assetsFile = new AssetsFile(fileName,
                    new EndianStream(File.OpenRead(fileName), EndianType.BigEndian));
                //if (Path.GetFileName(fileName) == "mainData") { mainDataFile = assetsFile; }

                totalAssetCount += assetsFile.preloadTable.Count;

                assetsfileList.Add(assetsFile);

                #region for 2.6.x find mainData and get string version

                if (assetsFile.fileGen == 6 && Path.GetFileName(fileName) != "mainData")
                {
                    AssetsFile mainDataFile = assetsfileList.Find(aFile =>
                        aFile.filePath == Path.GetDirectoryName(fileName) + "\\mainData");
                    if (mainDataFile != null)
                    {
                        assetsFile.m_Version = mainDataFile.m_Version;
                        assetsFile.version = mainDataFile.version;
                        assetsFile.buildType = mainDataFile.buildType;
                    }
                    else if (File.Exists(Path.GetDirectoryName(fileName) + "\\mainData"))
                    {
                        mainDataFile = new AssetsFile(Path.GetDirectoryName(fileName) + "\\mainData",
                            new EndianStream(File.OpenRead(Path.GetDirectoryName(fileName) + "\\mainData"),
                                EndianType.BigEndian));

                        assetsFile.m_Version = mainDataFile.m_Version;
                        assetsFile.version = mainDataFile.version;
                        assetsFile.buildType = mainDataFile.buildType;
                    }
                }

                #endregion

                progressBar1.PerformStep();

                foreach (AssetsFile.UnityShared sharedFile in assetsFile.sharedAssetsList)
                {
                    string sharedFilePath = Path.GetDirectoryName(fileName) + "\\" + sharedFile.fileName;
                    string sharedFileName = Path.GetFileName(sharedFile.fileName);
                    if (string.IsNullOrEmpty(sharedFileName))
                        continue;

                    //searching in unityFiles would preserve desired order, but...
                    string quedSharedFile = unityFiles.Find(uFile =>
                        string.Equals(Path.GetFileName(uFile), sharedFileName, StringComparison.OrdinalIgnoreCase));
                    if (quedSharedFile == null)
                    {
                        if (!File.Exists(sharedFilePath))
                        {
                            // ReSharper disable once AssignNullToNotNullAttribute
                            string[] findFiles = Directory.GetFiles(
                                Path.GetDirectoryName(fileName),
                                sharedFileName,
                                SearchOption.AllDirectories);
                            if (findFiles.Length > 0)
                            {
                                sharedFilePath = findFiles[0];
                            }
                        }

                        if (File.Exists(sharedFilePath))
                        {
                            //this would get screwed if the file somehow fails to load
                            sharedFile.Index = unityFiles.Count;
                            unityFiles.Add(sharedFilePath);
                            progressBar1.Maximum++;
                        }
                    }
                    else
                    {
                        sharedFile.Index = unityFiles.IndexOf(quedSharedFile);
                    }
                }
            }
        }

        private void LoadBundleFile(string bundleFileName)
        {
            StatusStripUpdate("Decompressing " + Path.GetFileName(bundleFileName) + "...");

            BundleFile b_File = new BundleFile(bundleFileName);

            List<AssetsFile> b_assetsfileList = new List<AssetsFile>();

            foreach (BundleFile.MemoryAssetsFile memFile in b_File.MemoryAssetsFileList) //filter unity files
            {
                bool validAssetsFile = false;
                switch (Path.GetExtension(memFile.fileName))
                {
                    case ".assets":
                    case ".sharedAssets":
                        validAssetsFile = true;
                        break;
                    case "":
                        validAssetsFile = memFile.fileName == "mainData" ||
                                          Regex.IsMatch(memFile.fileName, "level.*?") ||
                                          Regex.IsMatch(memFile.fileName, "CustomAssetBundle-.*?") ||
                                          Regex.IsMatch(memFile.fileName, "CAB-.*?") ||
                                          Regex.IsMatch(memFile.fileName, "BuildPlayer-.*?");
                        break;
                }

                if (!validAssetsFile)
                {
                    memFile.memStream.Close();
                    continue;
                }

                StatusStripUpdate("Loading " + memFile.fileName);
                //create dummy path to be used for asset extraction
                memFile.fileName = Path.GetDirectoryName(bundleFileName) + "\\" + memFile.fileName;

                AssetsFile assetsFile = new AssetsFile(memFile.fileName,
                    new EndianStream(memFile.memStream, EndianType.BigEndian));
                if (assetsFile.fileGen == 6 && Path.GetFileName(bundleFileName) != "mainData"
                ) //2.6.x and earlier don't have a string version before the preload table
                {
                    //make use of the bundle file version
                    assetsFile.m_Version = b_File.ver3;
                    assetsFile.version = Array.ConvertAll(
                        b_File.ver3.Split(new[]
                            {
                                ".", "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m",
                                "n", "o", "p", "q", "r", "s", "t", "u", "v", "w", "x", "y", "z", "\n"
                            },
                            StringSplitOptions.RemoveEmptyEntries),
                        int.Parse);

                    assetsFile.buildType = b_File.ver3.Split(new[]
                        {
                            ".", "0", "1", "2", "3", "4", "5", "6", "7", "8", "9"
                        },
                        StringSplitOptions.RemoveEmptyEntries);
                }

                b_assetsfileList.Add(assetsFile);
            }

            assetsfileList.AddRange(b_assetsfileList); // will the streams still be available for reading data?

            foreach (AssetsFile assetsFile in b_assetsfileList)
            {
                foreach (AssetsFile.UnityShared sharedFile in assetsFile.sharedAssetsList)
                {
                    sharedFile.fileName = Path.GetDirectoryName(bundleFileName) + "\\" + sharedFile.fileName;
                    AssetsFile loadedSharedFile = b_assetsfileList.Find(aFile => aFile.filePath == sharedFile.fileName);
                    if (loadedSharedFile != null)
                    {
                        sharedFile.Index = assetsfileList.IndexOf(loadedSharedFile);
                    }
                }
            }
        }

        private void extractBundleToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog openBundleDialog = new OpenFileDialog
            {
                Filter = "Unity bundle files|*.unity3d; *.unity3d.lz4; *.assetbundle; *.bundle; *.bytes|" +
                         "All files (use at your own risk!)|*.*",
                FilterIndex = 1,
                RestoreDirectory = true,
                Multiselect = true
            };

            if (openBundleDialog.ShowDialog() == DialogResult.OK)
            {
                int extractedCount = extractBundleFile(openBundleDialog.FileName);

                StatusStripUpdate("Finished extracting " + extractedCount.ToString() + " files.");
            }
        }

        private void extractFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            int extractedCount = 0;
            List<string> bundleFiles = new List<string>();

            if (openFolderDialog1.ShowDialog() == DialogResult.OK)
            {
                string startPath = openFolderDialog1.FileName;
                if (Path.GetFileName(startPath) == "Select folder")
                {
                    startPath = Path.GetDirectoryName(startPath);
                }

                string[] unityFileTypes =
                {
                    "*.unity3d",
                    "*.unity3d.lz4",
                    "*.assetbundle",
                    "*.assetbundle-*",
                    "*.bundle",
                    "*.bytes"
                };

                foreach (string fileType in unityFileTypes)
                {
                    // ReSharper disable once AssignNullToNotNullAttribute
                    string[] fileNames = Directory.GetFiles(startPath, fileType, SearchOption.AllDirectories);
                    bundleFiles.AddRange(fileNames);
                }

                extractedCount += bundleFiles.Sum(fileName => extractBundleFile(fileName));

                StatusStripUpdate("Finished extracting " + extractedCount.ToString() + " files.");
            }
        }

        private int extractBundleFile(string bundleFileName)
        {
            int extractedCount = 0;

            StatusStripUpdate("Decompressing " + Path.GetFileName(bundleFileName) + " ,,,");

            string extractPath = bundleFileName + "_unpacked\\";
            Directory.CreateDirectory(extractPath);

            BundleFile b_File = new BundleFile(bundleFileName);

            foreach (BundleFile.MemoryAssetsFile memFile in b_File.MemoryAssetsFileList)
            {
                string filePath = extractPath + memFile.fileName.Replace('/', '\\');
                if (!Directory.Exists(Path.GetDirectoryName(filePath)))
                {
                    // ReSharper disable once AssignNullToNotNullAttribute
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                }
                if (File.Exists(filePath))
                {
                    StatusStripUpdate("File " + memFile.fileName + " already exists");
                }
                else
                {
                    StatusStripUpdate("Extracting " + Path.GetFileName(memFile.fileName));
                    extractedCount += 1;

                    using (FileStream file = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                    {
                        memFile.memStream.WriteTo(file);
                        memFile.memStream.Close();
                    }
                }
            }

            return extractedCount;
        }

        private void UnityStudioForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.Alt && e.KeyCode == Keys.D)
            {
                debugMenuItem.Visible = !debugMenuItem.Visible;
                buildClassStructuresMenuItem.Checked = debugMenuItem.Visible;
                dontLoadAssetsMenuItem.Checked = debugMenuItem.Visible;
                dontBuildHierarchyMenuItem.Checked = debugMenuItem.Visible;
                if (tabControl1.TabPages.Contains(tabPage3))
                {
                    tabControl1.TabPages.Remove(tabPage3);
                }
                else
                {
                    tabControl1.TabPages.Add(tabPage3);
                }
            }
        }

        private void dontLoadAssetsMenuItem_CheckedChanged(object sender, EventArgs e)
        {
            if (dontLoadAssetsMenuItem.Checked)
            {
                dontBuildHierarchyMenuItem.Checked = true;
                dontBuildHierarchyMenuItem.Enabled = false;
            }
            else
            {
                dontBuildHierarchyMenuItem.Enabled = true;
            }
        }

        private void exportClassStructuresMenuItem_Click(object sender, EventArgs e)
        {
            if (AllClassStructures.Count > 0)
            {
                if (saveFolderDialog1.ShowDialog() == DialogResult.OK)
                {
                    progressBar1.Value = 0;
                    progressBar1.Maximum = AllClassStructures.Count;

                    string savePath = saveFolderDialog1.FileName;
                    if (Path.GetFileName(savePath) == "Select folder or write folder name to create")
                    {
                        savePath = Path.GetDirectoryName(saveFolderDialog1.FileName);
                    }

                    foreach (KeyValuePair<string, SortedDictionary<int, ClassStrStruct>> version in AllClassStructures)
                    {
                        if (version.Value.Count > 0)
                        {
                            string versionPath = savePath + "\\" + version.Key;
                            Directory.CreateDirectory(versionPath);

                            foreach (KeyValuePair<int, ClassStrStruct> uclass in version.Value)
                            {
                                string saveFile = versionPath + "\\" + uclass.Key + " " + uclass.Value.Text + ".txt";
                                using (StreamWriter TXTwriter = new StreamWriter(saveFile))
                                {
                                    TXTwriter.Write(uclass.Value.members);
                                }
                            }
                        }

                        progressBar1.PerformStep();
                    }

                    StatusStripUpdate("Finished exporting class structures");
                    progressBar1.Value = 0;
                }
            }
        }

        private void enablePreview_Check(object sender, EventArgs e)
        {
            if (lastLoadedAsset != null)
            {
                switch (lastLoadedAsset.Type2)
                {
                    case 28:
                    {
                        if (enablePreview.Checked && imageTexture != null)
                        {
                            previewPanel.BackgroundImage = imageTexture;
                            previewPanel.BackgroundImageLayout = ImageLayout.Zoom;
                        }
                        else
                        {
                            previewPanel.BackgroundImage = Properties.Resources.preview;
                            previewPanel.BackgroundImageLayout = ImageLayout.Center;
                        }
                    }
                        break;
                    case 48:
                    case 49:
                        textPreviewBox.Visible = !textPreviewBox.Visible;
                        break;
                    case 128:
                        fontPreviewBox.Visible = !fontPreviewBox.Visible;
                        break;
                    case 83:
                    {
                        FMODpanel.Visible = !FMODpanel.Visible;

                        if (sound != null && channel != null)
                        {
                            bool playing;
                            FMOD.RESULT result = channel.isPlaying(out playing);
                            if (result != FMOD.RESULT.OK && result != FMOD.RESULT.ERR_INVALID_HANDLE)
                            {
                                ERRCHECK(result);
                            }

                            if (playing)
                            {
                                //stop previous sound
                                result = channel.stop();
                                ERRCHECK(result);

                                FMODreset();
                            }
                            else if (enablePreview.Checked)
                            {
                                result = system.playSound(sound, null, false, out channel);
                                ERRCHECK(result);

                                timer.Start();
                                FMODstatusLabel.Text = "Playing";
                                //FMODinfoLabel.Text = FMODfrequency.ToString();
                            }
                        }

                        break;
                    }
                }
            }
            else if (lastSelectedItem != null && enablePreview.Checked)
            {
                lastLoadedAsset = lastSelectedItem;
                PreviewAsset(lastLoadedAsset);
            }

            Properties.Settings.Default["enablePreview"] = enablePreview.Checked;
            Properties.Settings.Default.Save();
        }

        private void displayAssetInfo_Check(object sender, EventArgs e)
        {
            if (displayInfo.Checked && assetInfoLabel.Text != null)
            {
                assetInfoLabel.Visible = true;
            }
            else
            {
                assetInfoLabel.Visible = false;
            }

            Properties.Settings.Default["displayInfo"] = displayInfo.Checked;
            Properties.Settings.Default.Save();
        }

        private void MenuItem_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default[((ToolStripMenuItem) sender).Name] = ((ToolStripMenuItem) sender).Checked;
            Properties.Settings.Default.Save();
        }

        private void assetGroupOptions_SelectedIndexChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default["assetGroupOption"] = ((ToolStripComboBox) sender).SelectedIndex;
            Properties.Settings.Default.Save();
        }

        private void showExpOpt_Click(object sender, EventArgs e)
        {
            ExportOptions exportOpt = new ExportOptions();
            exportOpt.ShowDialog();
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AboutBox aboutWindow = new AboutBox();
            aboutWindow.ShowDialog();
        }

        private void BuildAssetStrucutres()
        {
            #region first loop - read asset data & create list

            if (!dontLoadAssetsMenuItem.Checked)
            {
                assetListView.BeginUpdate();
                progressBar1.Value = 0;
                progressBar1.Maximum = totalAssetCount;

                string fileIDfmt = "D" + assetsfileList.Count.ToString().Length.ToString();

                foreach (AssetsFile assetsFile in assetsfileList)
                {
                    StatusStripUpdate("Building asset list from " + Path.GetFileName(assetsFile.filePath));

                    string fileID = assetsfileList.IndexOf(assetsFile).ToString(fileIDfmt);

                    //ListViewGroup assetGroup = new ListViewGroup(Path.GetFileName(assetsFile.filePath));


                    foreach (AssetPreloadData asset in assetsFile.preloadTable.Values)
                    {
                        asset.uniqueID = fileID + asset.uniqueID;

                        switch (asset.Type2)
                        {
                            case 1: //GameObject
                            {
                                GameObject m_GameObject = new GameObject(asset);
                                assetsFile.GameObjectList.Add(asset.m_PathID, m_GameObject);
                                totalTreeNodes++;
                                break;
                            }
                            case 4: //Transform
                            {
                                Transform m_Transform = new Transform(asset);
                                assetsFile.TransformList.Add(asset.m_PathID, m_Transform);
                                break;
                            }
                            case 224: //RectTransform
                            {
                                RectTransform m_Rect = new RectTransform(asset);
                                assetsFile.TransformList.Add(asset.m_PathID, m_Rect.m_Transform);
                                break;
                            }
                            //case 21: //Material
                            case 28: //Texture2D
                            {
                                Texture2D unused = new Texture2D(asset, false);
                                assetsFile.exportableAssets.Add(asset);
                                break;
                            }
                            case 48: //Shader
                            case 49: //TextAsset
                            {
                                TextAsset unused = new TextAsset(asset, false);
                                assetsFile.exportableAssets.Add(asset);
                                break;
                            }
                            case 83: //AudioClip
                            {
                                AudioClip unused = new AudioClip(asset, false);
                                assetsFile.exportableAssets.Add(asset);
                                break;
                            }
                            //case 89: //CubeMap
                            case 128: //Font
                            {
                                unityFont unused = new unityFont(asset, false);
                                assetsFile.exportableAssets.Add(asset);
                                break;
                            }
                            case 129: //PlayerSettings
                            {
                                PlayerSettings plSet = new PlayerSettings(asset);
                                productName = plSet.productName;
                                Text = "Unity Studio - " + productName + " - " +
                                       assetsFile.m_Version + " - " + assetsFile.platformStr;
                                break;
                            }
                            case 0:
                                break;
                        }

                        progressBar1.PerformStep();
                    }

                    exportableAssets.AddRange(assetsFile.exportableAssets);
                }

                if (Text == "Unity Studio" && assetsfileList.Count > 0)
                {
                    Text = "Unity Studio - no productName - " +
                           assetsfileList[0].m_Version + " - " + assetsfileList[0].platformStr;
                }

                visibleAssets = exportableAssets;
                assetListView.VirtualListSize = visibleAssets.Count;

                //will only work if ListView is visible
                resizeAssetListColumns();

                assetListView.EndUpdate();
                progressBar1.Value = 0;
            }

            #endregion

            #region second loop - build tree structure

            if (!dontBuildHierarchyMenuItem.Checked)
            {
                sceneTreeView.BeginUpdate();
                progressBar1.Value = 0;
                progressBar1.Maximum = totalTreeNodes;

                foreach (AssetsFile assetsFile in assetsfileList)
                {
                    StatusStripUpdate("Building tree structure from " + Path.GetFileName(assetsFile.filePath));
                    GameObject fileNode = new GameObject(null)
                    {
                        // ReSharper disable once AssignNullToNotNullAttribute
                        Text = Path.GetFileName(assetsFile.filePath),
                        m_Name = "RootNode"
                    };

                    foreach (GameObject m_GameObject in assetsFile.GameObjectList.Values)
                    {
                        GameObject parentNode = fileNode;

                        Transform m_Transform;
                        if (assetsfileList.TryGetTransform(m_GameObject.m_Transform, out m_Transform))
                        {
                            Transform m_Father;
                            if (assetsfileList.TryGetTransform(m_Transform.m_Father, out m_Father))
                            {
                                //GameObject Parent;
                                if (assetsfileList.TryGetGameObject(m_Father.m_GameObject, out parentNode))
                                {
                                    //parentNode = Parent;
                                }
                            }
                        }

                        parentNode.Nodes.Add(m_GameObject);
                        progressBar1.PerformStep();
                    }


                    if (fileNode.Nodes.Count == 0)
                    {
                        fileNode.Text += " (no children)";
                    }
                    sceneTreeView.Nodes.Add(fileNode);
                }
                sceneTreeView.EndUpdate();
                progressBar1.Value = 0;

                if (File.Exists(mainPath + "\\materials.json"))
                {
                    string matLine;
                    using (StreamReader reader = File.OpenText(mainPath + "\\materials.json"))
                    {
                        matLine = reader.ReadToEnd();
                    }

                    jsonMats =
                        new JavaScriptSerializer().Deserialize<Dictionary<string, Dictionary<string, string>>>(matLine);
                }
            }

            #endregion

            #region build list of class strucutres

            if (buildClassStructuresMenuItem.Checked)
            {
                //group class structures by versionv
                foreach (AssetsFile assetsFile in assetsfileList)
                {
                    SortedDictionary<int, ClassStrStruct> curVer;
                    if (AllClassStructures.TryGetValue(assetsFile.m_Version, out curVer))
                    {
                        foreach (KeyValuePair<int, ClassStrStruct> uClass in assetsFile.ClassStructures)
                        {
                            curVer[uClass.Key] = uClass.Value;
                        }
                    }
                    else
                    {
                        AllClassStructures.Add(assetsFile.m_Version, assetsFile.ClassStructures);
                    }
                }

                classesListView.BeginUpdate();
                foreach (KeyValuePair<string, SortedDictionary<int, ClassStrStruct>> version in AllClassStructures)
                {
                    ListViewGroup versionGroup = new ListViewGroup(version.Key);
                    classesListView.Groups.Add(versionGroup);

                    foreach (KeyValuePair<int, ClassStrStruct> uclass in version.Value)
                    {
                        uclass.Value.Group = versionGroup;
                        classesListView.Items.Add(uclass.Value);
                    }
                }
                classesListView.EndUpdate();
            }

            #endregion

            StatusStripUpdate("Finished loading " + assetsfileList.Count + " files with " +
                              assetListView.Items.Count + sceneTreeView.Nodes.Count + " exportable assets.");

            progressBar1.Value = 0;
            treeSearch.Select();

            saveFolderDialog1.InitialDirectory = mainPath;
        }

        private void assetListView_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            e.Item = visibleAssets[e.ItemIndex];
        }

        private void tabPageSelected(object sender, TabControlEventArgs e)
        {
            switch (e.TabPageIndex)
            {
                case 0:
                    treeSearch.Select();
                    break;
                case 1:
                    resizeAssetListColumns(); //required because the ListView is not visible on app launch
                    classPreviewPanel.Visible = false;
                    previewPanel.Visible = true;
                    listSearch.Select();
                    break;
                case 2:
                    previewPanel.Visible = false;
                    classPreviewPanel.Visible = true;
                    break;
            }
        }

        private void treeSearch_MouseEnter(object sender, EventArgs e)
        {
            treeTip.Show("Search with * ? widcards. Enter to scroll through results, Ctrl+Enter to select all results.",
                treeSearch, 5000);
        }

        private void treeSearch_Enter(object sender, EventArgs e)
        {
            if (treeSearch.Text == " Search ")
            {
                treeSearch.Text = "";
                treeSearch.ForeColor = SystemColors.WindowText;
            }
        }

        private void treeSearch_Leave(object sender, EventArgs e)
        {
            if (treeSearch.Text == "")
            {
                treeSearch.Text = " Search ";
                treeSearch.ForeColor = SystemColors.GrayText;
            }
        }

        private void recurseTreeCheck(TreeNodeCollection start)
        {
            foreach (GameObject GObject in start)
            {
                if (GObject.Text.Like(treeSearch.Text))
                {
                    GObject.Checked = !GObject.Checked;
                    if (GObject.Checked)
                    {
                        GObject.EnsureVisible();
                    }
                }
                else
                {
                    recurseTreeCheck(GObject.Nodes);
                }
            }
        }

        private void treeSearch_TextChanged(object sender, EventArgs e)
        {
            treeSrcResults.Clear();
            nextGObject = 0;
        }

        private void treeSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (treeSrcResults.Count == 0)
                {
                    foreach (AssetsFile aFile in assetsfileList)
                    {
                        foreach (GameObject GObject in aFile.GameObjectList.Values)
                        {
                            if (GObject.Text.Like(treeSearch.Text))
                            {
                                treeSrcResults.Add(GObject);
                            }
                        }
                    }
                }


                if (e.Control) //toggle all matching nodes
                {
                    sceneTreeView.BeginUpdate();
                    //loop TreeView recursively to avoid children already checked by parent
                    recurseTreeCheck(sceneTreeView.Nodes);
                    sceneTreeView.EndUpdate();
                }
                else //make visible one by one
                {
                    if (treeSrcResults.Count > 0)
                    {
                        if (nextGObject >= treeSrcResults.Count)
                        {
                            nextGObject = 0;
                        }
                        treeSrcResults[nextGObject].EnsureVisible();
                        sceneTreeView.SelectedNode = treeSrcResults[nextGObject];
                        nextGObject++;
                    }
                }
            }
        }

        private void sceneTreeView_AfterCheck(object sender, TreeViewEventArgs e)
        {
            foreach (GameObject childNode in e.Node.Nodes)
            {
                childNode.Checked = e.Node.Checked;
            }
        }

        private void resizeAssetListColumns()
        {
            assetListView.AutoResizeColumn(1, ColumnHeaderAutoResizeStyle.HeaderSize);
            assetListView.AutoResizeColumn(1, ColumnHeaderAutoResizeStyle.ColumnContent);
            assetListView.AutoResizeColumn(2, ColumnHeaderAutoResizeStyle.HeaderSize);
            assetListView.AutoResizeColumn(2, ColumnHeaderAutoResizeStyle.ColumnContent);

            int vscrollwidth = SystemInformation.VerticalScrollBarWidth;
            bool hasvscroll = visibleAssets.Count / (float) assetListView.Height > 0.0567f;
            columnHeaderName.Width = assetListView.Width
                                     - columnHeaderType.Width
                                     - columnHeaderSize.Width
                                     - (hasvscroll ? 5 + vscrollwidth : 5);
        }

        private void tabPage2_Resize(object sender, EventArgs e)
        {
            resizeAssetListColumns();
        }

        private void listSearch_Enter(object sender, EventArgs e)
        {
            if (listSearch.Text == " Filter ")
            {
                listSearch.Text = "";
                listSearch.ForeColor = SystemColors.WindowText;
                enableFiltering = true;
            }
        }

        private void listSearch_Leave(object sender, EventArgs e)
        {
            if (listSearch.Text == "")
            {
                enableFiltering = false;
                listSearch.Text = " Filter ";
                listSearch.ForeColor = SystemColors.GrayText;
            }
        }

        private void ListSearchTextChanged(object sender, EventArgs e)
        {
            if (enableFiltering)
            {
                assetListView.BeginUpdate();
                assetListView.SelectedIndices.Clear();
                visibleAssets = exportableAssets.FindAll(ListAsset =>
                    ListAsset.Text.IndexOf(listSearch.Text, StringComparison.CurrentCultureIgnoreCase) >= 0);
                assetListView.VirtualListSize = visibleAssets.Count;
                assetListView.EndUpdate();
            }
        }

        private void assetListView_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (firstSortColumn != e.Column)
            {
                //sorting column has been changed
                reverseSort = false;
                secondSortColumn = firstSortColumn;
            }
            else
            {
                reverseSort = !reverseSort;
            }
            firstSortColumn = e.Column;

            assetListView.BeginUpdate();
            assetListView.SelectedIndices.Clear();
            switch (e.Column)
            {
                case 0:
                    visibleAssets.Sort(delegate(AssetPreloadData a, AssetPreloadData b)
                    {
                        int xdiff = reverseSort
                            ? string.Compare(b.Text, a.Text, StringComparison.InvariantCulture)
                            : string.Compare(a.Text, b.Text, StringComparison.InvariantCulture);

                        if (xdiff != 0)
                            return xdiff;

                        return secondSortColumn == 1
                            ? string.Compare(a.TypeString, b.TypeString, StringComparison.InvariantCulture)
                            : a.exportSize.CompareTo(b.exportSize);
                    });
                    break;

                case 1:
                    visibleAssets.Sort(delegate(AssetPreloadData a, AssetPreloadData b)
                    {
                        int xdiff = reverseSort
                            ? string.Compare(b.TypeString, a.TypeString, StringComparison.InvariantCulture)
                            : string.Compare(a.TypeString, b.TypeString, StringComparison.InvariantCulture);

                        if (xdiff != 0)
                            return xdiff;

                        return secondSortColumn == 2
                            ? a.exportSize.CompareTo(b.exportSize)
                            : string.Compare(a.Text, b.Text, StringComparison.InvariantCulture);
                    });
                    break;

                case 2:
                    visibleAssets.Sort(delegate(AssetPreloadData a, AssetPreloadData b)
                    {
                        int xdiff = reverseSort
                            ? b.exportSize.CompareTo(a.exportSize)
                            : a.exportSize.CompareTo(b.exportSize);

                        if (xdiff != 0)
                            return xdiff;

                        return secondSortColumn == 1
                            ? string.Compare(a.TypeString, b.TypeString, StringComparison.InvariantCulture)
                            : string.Compare(a.Text, b.Text, StringComparison.InvariantCulture);
                    });
                    break;
            }

            assetListView.EndUpdate();

            resizeAssetListColumns();
        }

        private void selectAsset(object sender, ListViewItemSelectionChangedEventArgs e)
        {
            previewPanel.BackgroundImage = Properties.Resources.preview;
            previewPanel.BackgroundImageLayout = ImageLayout.Center;
            assetInfoLabel.Visible = false;
            assetInfoLabel.Text = null;
            textPreviewBox.Visible = false;
            fontPreviewBox.Visible = false;
            pfc.Dispose();
            FMODpanel.Visible = false;
            lastLoadedAsset = null;
            StatusStripUpdate("");

            FMODreset();

            lastSelectedItem = (AssetPreloadData) e.Item;

            if (e.IsSelected)
            {
                assetInfoLabel.Text = lastSelectedItem.InfoText;
                if (displayInfo.Checked && assetInfoLabel.Text != null)
                {
                    assetInfoLabel.Visible = true;
                } //only display the label if asset has info text

                if (enablePreview.Checked)
                {
                    lastLoadedAsset = lastSelectedItem;
                    PreviewAsset(lastLoadedAsset);
                }
            }
        }

        private void classesListView_ItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
        {
            if (e.IsSelected)
            {
                classTextBox.Text = ((ClassStrStruct) classesListView.SelectedItems[0]).members;
            }
        }

        private void PreviewAsset(AssetPreloadData asset)
        {
            switch (asset.Type2)
            {
                #region Texture2D

                case 28: //Texture2D
                {
                    Texture2D m_Texture2D = new Texture2D(asset, true);

                    if (m_Texture2D.m_TextureFormat < 30)
                    {
                        byte[] imageBuffer = new byte[128 + m_Texture2D.image_data_size];

                        imageBuffer[0] = 0x44;
                        imageBuffer[1] = 0x44;
                        imageBuffer[2] = 0x53;
                        imageBuffer[3] = 0x20;
                        imageBuffer[4] = 0x7c;

                        BitConverter.GetBytes(m_Texture2D.dwFlags).CopyTo(imageBuffer, 8);
                        BitConverter.GetBytes(m_Texture2D.m_Height).CopyTo(imageBuffer, 12);
                        BitConverter.GetBytes(m_Texture2D.m_Width).CopyTo(imageBuffer, 16);
                        BitConverter.GetBytes(m_Texture2D.dwPitchOrLinearSize).CopyTo(imageBuffer, 20);
                        BitConverter.GetBytes(m_Texture2D.dwMipMapCount).CopyTo(imageBuffer, 28);
                        BitConverter.GetBytes(m_Texture2D.dwSize).CopyTo(imageBuffer, 76);
                        BitConverter.GetBytes(m_Texture2D.dwFlags2).CopyTo(imageBuffer, 80);
                        BitConverter.GetBytes(m_Texture2D.dwFourCC).CopyTo(imageBuffer, 84);
                        BitConverter.GetBytes(m_Texture2D.dwRGBBitCount).CopyTo(imageBuffer, 88);
                        BitConverter.GetBytes(m_Texture2D.dwRBitMask).CopyTo(imageBuffer, 92);
                        BitConverter.GetBytes(m_Texture2D.dwGBitMask).CopyTo(imageBuffer, 96);
                        BitConverter.GetBytes(m_Texture2D.dwBBitMask).CopyTo(imageBuffer, 100);
                        BitConverter.GetBytes(m_Texture2D.dwABitMask).CopyTo(imageBuffer, 104);
                        BitConverter.GetBytes(m_Texture2D.dwCaps).CopyTo(imageBuffer, 108);
                        BitConverter.GetBytes(m_Texture2D.dwCaps2).CopyTo(imageBuffer, 112);

                        m_Texture2D.image_data.CopyTo(imageBuffer, 128);

                        imageTexture = DDSDataToBMP(imageBuffer);
                        imageTexture.RotateFlip(RotateFlipType.RotateNoneFlipY);
                        previewPanel.BackgroundImage = imageTexture;
                        previewPanel.BackgroundImageLayout = ImageLayout.Zoom;
                    }
                    else
                    {
                        StatusStripUpdate("Unsupported image for preview. Try to export.");
                    }
                    break;
                }

                #endregion

                #region AudioClip

                case 83: //AudioClip
                {
                    AudioClip m_AudioClip = new AudioClip(asset, true);

                    FMOD.CREATESOUNDEXINFO exinfo = new FMOD.CREATESOUNDEXINFO();

                    exinfo.cbsize = Marshal.SizeOf(exinfo);
                    exinfo.length = (uint) m_AudioClip.m_Size;

                    FMOD.RESULT result = system.createSound(
                        m_AudioClip.m_AudioData,
                        FMOD.MODE.OPENMEMORY | loopMode,
                        ref exinfo,
                        out sound);

                    if (ERRCHECK(result))
                        break;

                    result = sound.getLength(out FMODlenms, FMOD.TIMEUNIT.MS);
                    if (result != FMOD.RESULT.OK && result != FMOD.RESULT.ERR_INVALID_HANDLE)
                    {
                        if (ERRCHECK(result))
                            break;
                    }

                    result = system.playSound(sound, null, false, out channel);
                    if (ERRCHECK(result))
                        break;

                    timer.Start();
                    FMODstatusLabel.Text = "Playing";
                    FMODpanel.Visible = true;

                    //result = channel.getChannelGroup(out channelGroup);
                    //if (ERRCHECK(result)) { break; }

                    result = channel.getFrequency(out FMODfrequency);
                    ERRCHECK(result);

                    FMODinfoLabel.Text = FMODfrequency + " Hz";
                    break;
                }

                #endregion

                #region Shader & TextAsset

                case 48:
                case 49:
                {
                    TextAsset m_TextAsset = new TextAsset(asset, true);

                    string m_Script_Text = Encoding.UTF8.GetString(m_TextAsset.m_Script);
                    m_Script_Text = Regex.Replace(m_Script_Text, "(?<!\r)\n", "\r\n");
                    textPreviewBox.Text = m_Script_Text;
                    textPreviewBox.Visible = true;

                    break;
                }

                #endregion

                #region Font

                case 128: //Font
                {
                    unityFont m_Font = new unityFont(asset, true);

                    if (asset.extension != ".otf" && m_Font.m_FontData != null)
                    {
                        IntPtr data = Marshal.AllocCoTaskMem(m_Font.m_FontData.Length);
                        Marshal.Copy(m_Font.m_FontData, 0, data, m_Font.m_FontData.Length);

                        // We HAVE to do this to register the font to the system (Weird .NET bugs)
                        uint cFonts = 0;
                        AddFontMemResourceEx(data, (uint) m_Font.m_FontData.Length, IntPtr.Zero, ref cFonts);

                        pfc = new System.Drawing.Text.PrivateFontCollection();
                        pfc.AddMemoryFont(data, m_Font.m_FontData.Length);
                        Marshal.FreeCoTaskMem(data);

                        if (pfc.Families.Length > 0)
                        {
                            fontPreviewBox.SelectionStart = 0;
                            fontPreviewBox.SelectionLength = 80;
                            fontPreviewBox.SelectionFont = new Font(pfc.Families[0], 16, FontStyle.Regular);
                            fontPreviewBox.SelectionStart = 81;
                            fontPreviewBox.SelectionLength = 56;
                            fontPreviewBox.SelectionFont = new Font(pfc.Families[0], 12, FontStyle.Regular);
                            fontPreviewBox.SelectionStart = 138;
                            fontPreviewBox.SelectionLength = 56;
                            fontPreviewBox.SelectionFont = new Font(pfc.Families[0], 18, FontStyle.Regular);
                            fontPreviewBox.SelectionStart = 195;
                            fontPreviewBox.SelectionLength = 56;
                            fontPreviewBox.SelectionFont = new Font(pfc.Families[0], 24, FontStyle.Regular);
                            fontPreviewBox.SelectionStart = 252;
                            fontPreviewBox.SelectionLength = 56;
                            fontPreviewBox.SelectionFont = new Font(pfc.Families[0], 36, FontStyle.Regular);
                            fontPreviewBox.SelectionStart = 309;
                            fontPreviewBox.SelectionLength = 56;
                            fontPreviewBox.SelectionFont = new Font(pfc.Families[0], 48, FontStyle.Regular);
                            fontPreviewBox.SelectionStart = 366;
                            fontPreviewBox.SelectionLength = 56;
                            fontPreviewBox.SelectionFont = new Font(pfc.Families[0], 60, FontStyle.Regular);
                            fontPreviewBox.SelectionStart = 423;
                            fontPreviewBox.SelectionLength = 55;
                            fontPreviewBox.SelectionFont = new Font(pfc.Families[0], 72, FontStyle.Regular);
                            fontPreviewBox.Visible = true;
                        }
                    }
                    else
                    {
                        StatusStripUpdate("Unsupported font for preview. Try to export.");
                    }

                    break;
                }

                #endregion
            }
        }

        private void Export3DObjects_Click(object sender, EventArgs e)
        {
            if (sceneTreeView.Nodes.Count > 0)
            {
                bool exportSwitch = ((ToolStripItem) sender).Name == "exportAll3DMenuItem";

                DateTime timestamp = DateTime.Now;
                saveFileDialog1.FileName = productName + timestamp.ToString("_yy_MM_dd__HH_mm_ss");

                //extension will be added by the file save dialog
                if (saveFileDialog1.ShowDialog() == DialogResult.OK)
                {
                    switch ((bool) Properties.Settings.Default["showExpOpt"])
                    {
                        case true:
                            ExportOptions exportOpt = new ExportOptions();
                            if (exportOpt.ShowDialog() == DialogResult.OK)
                            {
                                goto case false;
                            }
                            break;
                        case false:
                            switch (saveFileDialog1.FilterIndex)
                            {
                                case 1:
                                    WriteFBX(saveFileDialog1.FileName, exportSwitch);
                                    break;
                                case 2:
                                    break;
                            }

                            if (openAfterExport.Checked && File.Exists(saveFileDialog1.FileName))
                            {
                                System.Diagnostics.Process.Start(saveFileDialog1.FileName);
                            }
                            break;
                    }
                }
            }
            else
            {
                StatusStripUpdate("No Objects available for export");
            }
        }

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
                /*
                uniqueIDs can begin with zero, so they are preceded by a number specific to their type
                this will also make it easier to debug FBX files
                1: Model
                2: NodeAttribute
                3: Geometry
                4: Deformer
                5: CollectionExclusive
                6: Material
                7: Texture
                8: Video
                9:
                */

                #region loop nodes and collect objects for export

                foreach (AssetsFile assetsFile in assetsfileList)
                {
                    foreach (GameObject m_GameObject in assetsFile.GameObjectList.Values)
                    {
                        if (m_GameObject.Checked || allNodes)
                        {
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

                                    //write connections here and Mesh objects separately without having to backtrack through their MEshFilter to het the GameObject ID
                                    //also note that MeshFilters are not unique, they cannot be used for instancing geometry
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
                            if (assetsfileList.TryGetPD(m_GameObject.m_SkinnedMeshRenderer, out SkinnedMeshPD))
                            {
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

                                if ((bool) Properties.Settings.Default["exportDeformers"])
                                {
                                    DeformerCount += m_SkinnedMeshRenderer.m_Bones.Length;

                                    //collect skeleton dummies to make sure they are exported
                                    foreach (PPtr bonePPtr in m_SkinnedMeshRenderer.m_Bones)
                                    {
                                        Transform b_Transform;
                                        if (assetsfileList.TryGetTransform(bonePPtr, out b_Transform))
                                        {
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
                                                if (assetsfileList.TryGetTransform(ChildPPtr, out ChildTR))
                                                {
                                                    GameObject m_Child;
                                                    if (assetsfileList.TryGetGameObject(ChildTR.m_GameObject,
                                                        out m_Child))
                                                    {
                                                        //check that the Model doesn't contain a Mesh, although this won't ensure it's part of the skeleton
                                                        if (m_Child.m_MeshFilter == null &&
                                                            m_Child.m_SkinnedMeshRenderer == null)
                                                        {
                                                            LimbNodes.Add(m_Child);
                                                        }
                                                    }
                                                }
                                            }

                                            #endregion
                                        }
                                    }
                                }
                            }

                            #endregion
                        }
                    }
                }

                //if ((bool)Properties.Settings.Default["convertDummies"]) { GameObjects.Except(LimbNodes); }
                //else { GameObjects.UnionWith(LimbNodes); LimbNodes.Clear(); }
                //add either way and use LimbNodes to test if a node is Null or LimbNode
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
                    if (m_Mesh.m_Skin.Length <= 0 ||
                        m_Mesh.m_BindPose.Length < m_SkinnedMeshRenderer.m_Bones.Length)
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
                        if (assetsfileList.TryGetTransform(m_SkinnedMeshRenderer.m_Bones[b],
                            out m_Transform))
                        {
                            GameObject m_Bone;
                            if (assetsfileList.TryGetGameObject(m_Transform.m_GameObject, out m_Bone))
                            {
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
                                    if (influence != null)
                                    {
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
                                //ob.Append(string.Join(",", m_Mesh.m_BindPose[b]));
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
                                cb2.AppendFormat("\n\tC: \"OO\",1{0},4{1}{2}", m_Bone.uniqueID, b,
                                    SkinnedMeshPD.uniqueID);
                            }
                        }
                    }
                }

                if ((bool) Properties.Settings.Default["exportDeformers"])
                {
                    foreach (GameObject m_Bone in LimbNodes)
                    {
                        //add BindPose node
                        pb.Append("\n\t\tPoseNode:  {");
                        pb.AppendFormat("\n\t\t\tNode: 1{0}", m_Bone.uniqueID);
                        //pb.Append("\n\t\t\tMatrix: *16 {");
                        //pb.Append("\n\t\t\t\ta: ");
                        //pb.Append("\n\t\t\t} ");
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
                    ((float) randomColor[0] / 255), ((float) randomColor[1] / 255), ((float) randomColor[2] / 255));
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

                //in order to test topology for triangles/quads we need to store submeshes and write each one as geometry, then link to Mesh Node
                ob.AppendFormat("\n\t\tPolygonVertexIndex: *{0} {{\n\t\t\ta: ", m_Mesh.m_Indices.Count);

                lineSplit = ob.Length;
                for (int f = 0; f < m_Mesh.m_Indices.Count / 3; f++)
                {
                    ob.AppendFormat("{0},{1},{2},", m_Mesh.m_Indices[f * 3], m_Mesh.m_Indices[f * 3 + 2],
                        (-m_Mesh.m_Indices[f * 3 + 1] - 1));

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
                    ob.AppendFormat("\n\t\t\tNormals: *{0} {{\n\t\t\ta: ", (m_Mesh.m_VertexCount * 3));

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
                    ob.AppendFormat("\n\t\t\tTangents: *{0} {{\n\t\t\ta: ", (m_Mesh.m_VertexCount * 3));

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
                    for (int i = 0; i < m_Mesh.m_materialIDs.Count; i++)
                    {
                        ob.AppendFormat("{0},", m_Mesh.m_materialIDs[i]);

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
                //
                /*ob.Append("\n\t\t\tLayerElement:  {");
                ob.Append("\n\t\t\t\tType: \"LayerElementTexture\"");
                ob.Append("\n\t\t\t\tTypedIndex: 0");
                ob.Append("\n\t\t\t}");
                ob.Append("\n\t\t\tLayerElement:  {");
                ob.Append("\n\t\t\t\tType: \"LayerElementBumpTextures\"");
                ob.Append("\n\t\t\t\tTypedIndex: 0");
                ob.Append("\n\t\t\t}");*/
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

        private void ExportAssets_Click(object sender, EventArgs e)
        {
            if (exportableAssets.Count > 0 && saveFolderDialog1.ShowDialog() == DialogResult.OK)
            {
                string savePath = saveFolderDialog1.FileName;
                if (Path.GetFileName(savePath) == "Select folder or write folder name to create")
                {
                    savePath = Path.GetDirectoryName(saveFolderDialog1.FileName);
                }

                bool exportAll = ((ToolStripItem) sender).Name == "exportAllAssetsMenuItem";
                bool exportFiltered = ((ToolStripItem) sender).Name == "exportFilteredAssetsMenuItem";
                bool exportSelected = ((ToolStripItem) sender).Name == "exportSelectedAssetsMenuItem";

                int toExport = 0;
                int exportedCount = 0;

                //looping assetsFiles will optimize HDD access
                //but will also have a small performance impact when exporting only a couple of selected assets
                foreach (AssetsFile assetsFile in assetsfileList)
                {
                    string exportpath = savePath + "\\";
                    if (assetGroupOptions.SelectedIndex == 1)
                    {
                        exportpath += Path.GetFileNameWithoutExtension(assetsFile.filePath) + "_export\\";
                    }

                    foreach (AssetPreloadData asset in assetsFile.exportableAssets)
                    {
                        if (exportAll ||
                            visibleAssets.Exists(x => x.uniqueID == asset.uniqueID) && (exportFiltered ||
                                                                                        exportSelected &&
                                                                                        asset.Index >= 0 &&
                                                                                        assetListView.SelectedIndices
                                                                                            .Contains(asset.Index)))
                        {
                            toExport++;
                            if (assetGroupOptions.SelectedIndex == 0)
                            {
                                exportpath = savePath + "\\" + asset.TypeString + "\\";
                            }

                            //AudioClip and Texture2D extensions are set when the list is built
                            //so their overwrite tests can be done without loading them again

                            switch (asset.Type2)
                            {
                                case 28:
                                    if (!ExportFileExists(exportpath + asset.Text + asset.extension, asset.TypeString))
                                    {
                                        ExportTexture(new Texture2D(asset, true),
                                            exportpath + asset.Text + asset.extension);
                                        exportedCount++;
                                    }
                                    break;
                                case 83:
                                    if (!ExportFileExists(exportpath + asset.Text + asset.extension, asset.TypeString))
                                    {
                                        ExportAudioClip(new AudioClip(asset, true),
                                            exportpath + asset.Text + asset.extension);
                                        exportedCount++;
                                    }
                                    break;
                                case 48:
                                    if (!ExportFileExists(exportpath + asset.Text + ".txt", asset.TypeString))
                                    {
                                        ExportText(new TextAsset(asset, true), exportpath + asset.Text + ".txt");
                                        exportedCount++;
                                    }
                                    break;
                                case 49:
                                    TextAsset m_TextAsset = new TextAsset(asset, true);
                                    if (!ExportFileExists(exportpath + asset.Text + asset.extension, asset.TypeString))
                                    {
                                        ExportText(m_TextAsset, exportpath + asset.Text + asset.extension);
                                        exportedCount++;
                                    }
                                    break;
                                case 128:
                                    unityFont m_Font = new unityFont(asset, true);
                                    if (!ExportFileExists(exportpath + asset.Text + asset.extension, asset.TypeString))
                                    {
                                        ExportFont(m_Font, exportpath + asset.Text + asset.extension);
                                        exportedCount++;
                                    }
                                    break;
                            }
                        }
                    }
                }

                string statusText = "";
                switch (exportedCount)
                {
                    case 0:
                        statusText = "Nothing exported.";
                        break;
                    case 1:
                        statusText = toolStripStatusLabel1.Text + " finished.";
                        break;
                    default:
                        statusText = "Finished exporting " + exportedCount.ToString() + " assets.";
                        break;
                }

                if (toExport > exportedCount)
                {
                    statusText += " " + (toExport - exportedCount).ToString() +
                                  " assets skipped (not extractable or files already exist)";
                }

                StatusStripUpdate(statusText);

                if (openAfterExport.Checked && exportedCount > 0)
                {
                    System.Diagnostics.Process.Start(savePath);
                }
            }
            else
            {
                StatusStripUpdate("No exportable assets loaded");
            }
        }

        private bool ExportFileExists(string filename, string assetType)
        {
            if (File.Exists(filename))
            {
                StatusStripUpdate(assetType + " file " + Path.GetFileName(filename) + " already exists");
                return true;
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filename));

                StatusStripUpdate("Exporting " + assetType + Path.GetFileName(filename));
                return false;
            }
        }

        private void StatusStripUpdate(string statusText)
        {
            toolStripStatusLabel1.Text = statusText;
            statusStrip1.Update();
        }

        public UnityStudioForm()
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            InitializeComponent();
            uniqueNames.Checked = (bool) Properties.Settings.Default["uniqueNames"];
            displayInfo.Checked = (bool) Properties.Settings.Default["displayInfo"];
            enablePreview.Checked = (bool) Properties.Settings.Default["enablePreview"];
            openAfterExport.Checked = (bool) Properties.Settings.Default["openAfterExport"];
            assetGroupOptions.SelectedIndex = (int) Properties.Settings.Default["assetGroupOption"];
            FMODinit();
        }

        private void resetForm()
        {
            /*Properties.Settings.Default["uniqueNames"] = uniqueNamesMenuItem.Checked;
            Properties.Settings.Default["enablePreview"] = enablePreviewMenuItem.Checked;
            Properties.Settings.Default["displayInfo"] = displayAssetInfoMenuItem.Checked;
            Properties.Settings.Default.Save();*/

            base.Text = "Unity Studio";

            unityFiles.Clear();
            assetsfileList.Clear();
            exportableAssets.Clear();
            visibleAssets.Clear();

            sceneTreeView.Nodes.Clear();

            assetListView.VirtualListSize = 0;
            assetListView.Items.Clear();
            //assetListView.Groups.Clear();

            classesListView.Items.Clear();
            classesListView.Groups.Clear();

            previewPanel.BackgroundImage = Properties.Resources.preview;
            previewPanel.BackgroundImageLayout = ImageLayout.Center;
            assetInfoLabel.Visible = false;
            assetInfoLabel.Text = null;
            textPreviewBox.Visible = false;
            fontPreviewBox.Visible = false;
            lastSelectedItem = null;
            lastLoadedAsset = null;
            firstSortColumn = -1;
            secondSortColumn = 0;
            reverseSort = false;
            enableFiltering = false;

            //FMODinit();
            FMODreset();
        }

        private void UnityStudioForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            /*Properties.Settings.Default["uniqueNames"] = uniqueNamesMenuItem.Checked;
            Properties.Settings.Default["enablePreview"] = enablePreviewMenuItem.Checked;
            Properties.Settings.Default["displayInfo"] = displayAssetInfoMenuItem.Checked;
            Properties.Settings.Default.Save();

            foreach (var assetsFile in assetsfileList) { assetsFile.a_Stream.Dispose(); } //is this needed?*/
        }
    }
}
