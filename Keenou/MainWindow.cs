﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Security.Principal;
using System.IO;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using System.Security.Cryptography;
using System.Threading;

namespace Keenou
{
    public partial class MainWindow : Form
    {
        const int CIPHER_C_DEFAULT = 0;
        const int HASH_C_DEFAULT = 2;
        const int VOLUME_SIZE_MULT_DEFAULT = 3;

        protected string[] ciphers = { "AES", "Serpent", "Twofish", "AES(Twofish)", "AES(Twofish(Serpent))", "Serpent(AES)", "Serpent(Twofish(AES))", "Twofish(Serpent)" };
        protected string[] hashes = { "sha256", "sha512", "whirlpool", "ripemd160" };

        protected string defaultVolumeLoc = string.Empty;
        protected string homeFolder = string.Empty;
        protected string username = string.Empty;
        protected string usrSID = string.Empty;
        protected long homeDirSize = 0;




        // Constructor //
        public MainWindow()
        {
            InitializeComponent();


            // Get user name
            this.username = Environment.UserName.ToString();

            // Get user SID
            NTAccount acct = new NTAccount(username);
            SecurityIdentifier s = (SecurityIdentifier)acct.Translate(typeof(SecurityIdentifier));
            this.usrSID = s.ToString();

            // Get user home directory 
            this.homeFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // Get volume location (default) 
            this.defaultVolumeLoc = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System)) + this.username + ".hc";




            // Figure out where the home folder's encrypted file is located for this user //
            string encDrive = (string)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Keenou\" + this.usrSID, "encDrive", string.Empty);
            string encContainerLoc = (string)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Keenou\" + this.usrSID, "encContainerLoc", string.Empty);
            if (!string.IsNullOrWhiteSpace(encContainerLoc) && !string.IsNullOrWhiteSpace(encDrive) && Directory.Exists(encDrive + @":\"))
            {
                // We're already running in an encrypted home directory environment! 
                g_homeDirectory.Enabled = false;
                l_homeAlreadyEncrypted.Visible = true;
                l_homeAlreadyEncrypted.Enabled = true;
            }
            // * //



            l_statusLabel.Text = "Ready ...";
            Application.DoEvents();
        }
        // * //



        // Internal events  // 
        private void MainWindow_Load(object sender, EventArgs e)
        {

            // Choose defaults  
            c_cipher.SelectedIndex = CIPHER_C_DEFAULT;
            c_hash.SelectedIndex = HASH_C_DEFAULT;


            // Fill out user name and SID
            t_userName.Text = this.username;
            t_sid.Text = this.usrSID;


            // Set output volume location
            t_volumeLoc.Text = this.defaultVolumeLoc;

        }
        // * //



        // When user hits the encrypt button for Home Folder //
        private void ReportEncryptHomeError(BooleanResult res)
        {
            if (res.Message != null)
            {
                MessageBox.Show(res.Message);
            }

            // Reset state of window, and display error conditions 
            this.Cursor = Cursors.Default;
            g_homeDirectory.Enabled = true;
            l_statusLabel.Text = "ERROR";
            s_progress.Value = 0;
            s_progress.Visible = false;
        }
        private void b_encrypt_Click(object sender, EventArgs e)
        {

            // Sanity checks //
            if (string.IsNullOrWhiteSpace(t_volumeSize.Text))
            {
                ReportEncryptHomeError(new BooleanResult() { Success = false, Message = "Please specify a volume size!" });
                return;
            }
            if (t_password.Text.Length <= 0 || !string.Equals(t_password.Text, t_passwordConf.Text))
            {
                ReportEncryptHomeError(new BooleanResult() { Success = false, Message = "Passwords provided must match and be non-zero in length!" });
                return;
            }
            if (t_volumeLoc.Text.Length <= 0)
            {
                ReportEncryptHomeError(new BooleanResult() { Success = false, Message = "Please specify a encrypted volume location!" });
                return;
            }
            if (t_volumeLoc.Text.Contains(this.homeFolder))
            {
                ReportEncryptHomeError(new BooleanResult() { Success = false, Message = "You cannot put your encrypted volume in your home directory!" });
                return;
            }
            if (c_hash.SelectedIndex < 0)
            {
                ReportEncryptHomeError(new BooleanResult() { Success = false, Message = "Please choose a hash!" });
                return;
            }
            if (c_cipher.SelectedIndex < 0)
            {
                ReportEncryptHomeError(new BooleanResult() { Success = false, Message = "Please choose a cipher!" });
                return;
            }
            // TODO: warn user if volume size will not fit home directory
            // * //



            // Helper result object
            BooleanResult res = null;


            // Get user-specified values 
            string hashChosen = c_hash.SelectedItem.ToString();
            string cipherChosen = c_cipher.SelectedItem.ToString();
            long volSize = Int64.Parse(t_volumeSize.Text);


            // Progress bar 
            s_progress.Value = 0;
            s_progress.Visible = true;
            Application.DoEvents();
            s_progress.ProgressBar.Refresh();


            // Ensure there will be enough space for the enc volume
            if (volSize > Toolbox.GetAvailableFreeSpace(t_volumeLoc.Text))
            {
                ReportEncryptHomeError(new BooleanResult() { Success = false, Message = "ERROR: Your encrypted volume will not fit on the chosen target drive!" });
                return;
            }



            // Disable while we calcualte stuff 
            this.Cursor = Cursors.WaitCursor;
            g_homeDirectory.Enabled = false;



            // GET NEXT FREE DRIVE LETTER 
            string targetDrive = null;

            char[] alpha = "VTHEDFGIJKLMNOPQRSUWXYZC".ToCharArray();
            string[] taken = Directory.GetLogicalDrives();
            foreach (char dL in alpha)
            {
                int pos = Array.IndexOf(taken, dL + @":\");
                if (pos == -1)
                {
                    targetDrive = dL.ToString();
                    break;
                }
            }
            if (targetDrive == null)
            {
                ReportEncryptHomeError(new BooleanResult() { Success = false, Message = "ERROR: Cannot find a free drive letter!" });
                return;
            }
            // * //





            // Create new encrypted volume //
            l_statusLabel.Text = "Creating encrypted volume ...";
            Application.DoEvents();
            res = EncryptHome.CreateEncryptedVolume(hashChosen, t_volumeLoc.Text, targetDrive, Toolbox.SHA512_Base64(t_password.Text), cipherChosen, volSize);
            if (res == null || !res.Success)
            {
                ReportEncryptHomeError(res);
                return;
            }
            // * //



            // Mount home folder's encrypted file as targetDrive //
            s_progress.Value = 33;
            l_statusLabel.Text = "Mounting encrypted volume ...";
            Application.DoEvents();
            s_progress.ProgressBar.Refresh();
            res = EncryptHome.MountEncryptedVolume(hashChosen, t_volumeLoc.Text, targetDrive, Toolbox.SHA512_Base64(t_password.Text));
            if (res == null || !res.Success)
            {
                ReportEncryptHomeError(res);
                return;
            }
            // * //



            // Copy everything over from home directory to encrypted container //
            s_progress.Value = 66;
            l_statusLabel.Text = "Copying home directory to encrypted container ...";
            Application.DoEvents();
            s_progress.ProgressBar.Refresh();
            res = EncryptHome.CopyDataFromHomeFolder(this.homeFolder, targetDrive);
            if (res == null || !res.Success)
            {
                ReportEncryptHomeError(res);
                return;
            }
            // * //



            // TODO: Unmount encrypted volume (after beta testing over) 
            // VeraCrypt should auto-unmount, but we'll do it manually to be sure 



            // Set necessary registry values //
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Keenou\" + this.usrSID, "encContainerLoc", t_volumeLoc.Text);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Keenou\" + this.usrSID, "firstBoot", true, RegistryValueKind.DWord);
            Registry.SetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Keenou\" + this.usrSID, "hash", hashChosen);
            // * //



            // Re-enable everything //
            this.Cursor = Cursors.Default;
            l_statusLabel.Text = "Log out and back in to finish ...";
            s_progress.Value = 100;
            Application.DoEvents();
            // * //



            // Inform user of the good news 
            MessageBox.Show("Almost done!  You must log out and log back in via Keenou-pGina to finish the migration!");
        }
        // * //



        // When user hits "Choose" box to override default volume location //
        private void b_volumeLoc_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog outputFileDialog = new OpenFileDialog())
            {
                outputFileDialog.InitialDirectory = this.defaultVolumeLoc;
                outputFileDialog.FilterIndex = 0;
                outputFileDialog.CheckFileExists = false;
                outputFileDialog.RestoreDirectory = true;

                try
                {
                    if (outputFileDialog.ShowDialog(this) == DialogResult.OK)
                    {
                        t_volumeLoc.Text = outputFileDialog.FileName;

                        if (File.Exists(outputFileDialog.FileName))
                        {
                            MessageBox.Show("Warning: File already exists and will be overwritten!");
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("[" + ex.GetType().Name + "] " + ex.Message + ex.StackTrace);
                }
            }
        }
        // * //



        // User wants us to suggest a volume size to them //
        private void b_setVolumeSize_Click(object sender, EventArgs e)
        {
            // Disable while we calcualte stuff 
            this.Cursor = Cursors.WaitCursor;
            g_homeDirectory.Enabled = false;
            l_statusLabel.Text = "Calculating your home directory size  ...";
            Application.DoEvents();


            // Determine free space on enc volume target drive 
            long targetSpace = Toolbox.GetAvailableFreeSpace(t_volumeLoc.Text);


            // Do calculation of current size (if not already done) 
            if (this.homeDirSize <= 0)
            {
                this.homeDirSize = Toolbox.GetDirectorySize(this.homeFolder);
            }


            // Show suggested volume size 
            long volSizeSuggested = (VOLUME_SIZE_MULT_DEFAULT * this.homeDirSize / (1024 * 1024));
            t_volumeSize.Text = volSizeSuggested.ToString();


            // If not enough space, alert user
            if (volSizeSuggested >= targetSpace)
            {
                string targetDrive = Path.GetPathRoot(t_volumeLoc.Text);
                MessageBox.Show("Warning: There is not enough space on the " + targetDrive + " drive! ");
            }


            // Re-enable everything 
            this.Cursor = Cursors.Default;
            g_homeDirectory.Enabled = true;
            l_statusLabel.Text = "Ready ...";
            Application.DoEvents();
        }
        // * //



    } // End MainWindow class 

    // End namespace 
}
