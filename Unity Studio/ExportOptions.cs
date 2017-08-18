﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace UnityStudio
{
    public partial class ExportOptions : Form
    {
        public string selectedPath = "";

        public ExportOptions()
        {
            InitializeComponent();
            exportNormals.Checked = (bool)Properties.Settings.Default["exportNormals"];
            exportTangents.Checked = (bool)Properties.Settings.Default["exportTangents"];
            exportUVs.Checked = (bool)Properties.Settings.Default["exportUVs"];
            exportColors.Checked = (bool)Properties.Settings.Default["exportColors"];
            exportDeformers.Checked = (bool)Properties.Settings.Default["exportDeformers"];
            convertDummies.Checked = (bool)Properties.Settings.Default["convertDummies"];
            convertDummies.Enabled = (bool)Properties.Settings.Default["exportDeformers"];
            scaleFactor.Value = (decimal)Properties.Settings.Default["scaleFactor"];
            upAxis.SelectedIndex = (int)Properties.Settings.Default["upAxis"];
            showExpOpt.Checked = (bool)Properties.Settings.Default["showExpOpt"];
        }

        private void exportOpnions_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default[((CheckBox)sender).Name] = ((CheckBox)sender).Checked;
            Properties.Settings.Default.Save();
        }

        private void fbxOKbutton_Click(object sender, EventArgs e)
        {
            Properties.Settings.Default["exportNormals"] = exportNormals.Checked;
            Properties.Settings.Default["exportTangents"] = exportTangents.Checked;
            Properties.Settings.Default["exportUVs"] = exportUVs.Checked;
            Properties.Settings.Default["exportColors"] = exportColors.Checked;
            Properties.Settings.Default["exportDeformers"] = exportDeformers.Checked;
            Properties.Settings.Default["scaleFactor"] = scaleFactor.Value;
            Properties.Settings.Default["upAxis"] = upAxis.SelectedIndex;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void fbxCancel_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        private void exportDeformers_CheckedChanged(object sender, EventArgs e)
        {
            exportOpnions_CheckedChanged(sender, e);
            convertDummies.Enabled = exportDeformers.Checked;
        }
    }
}
