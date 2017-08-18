using System;
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
            if (sceneTreeView.Nodes.Count <= 0)
            {
                StatusStripUpdate("No Objects available for export");
                return;
            }

            bool exportSwitch = ((ToolStripItem) sender).Name == "exportAll3DMenuItem";

            DateTime timestamp = DateTime.Now;
            saveFileDialog1.FileName = productName + timestamp.ToString("_yy_MM_dd__HH_mm_ss");

            //extension will be added by the file save dialog
            if (saveFileDialog1.ShowDialog() != DialogResult.OK)
                return;

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

                string statusText;
                switch (exportedCount)
                {
                    case 0:
                        statusText = "Nothing exported.";
                        break;
                    case 1:
                        statusText = toolStripStatusLabel1.Text + " finished.";
                        break;
                    default:
                        statusText = "Finished exporting " + exportedCount + " assets.";
                        break;
                }

                if (toExport > exportedCount)
                {
                    statusText += " " + (toExport - exportedCount) +
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

            // ReSharper disable once AssignNullToNotNullAttribute
            Directory.CreateDirectory(Path.GetDirectoryName(filename));
            StatusStripUpdate("Exporting " + assetType + Path.GetFileName(filename));
            return false;
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
            Text = "Unity Studio";

            unityFiles.Clear();
            assetsfileList.Clear();
            exportableAssets.Clear();
            visibleAssets.Clear();

            sceneTreeView.Nodes.Clear();

            assetListView.VirtualListSize = 0;
            assetListView.Items.Clear();

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

            FMODreset();
        }

        private void UnityStudioForm_FormClosing(object sender, FormClosingEventArgs e)
        {
        }
    }
}
