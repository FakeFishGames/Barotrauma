namespace Launcher
{
    partial class LauncherMain
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(LauncherMain));
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.launchButton = new System.Windows.Forms.Button();
            this.resolutionBox = new System.Windows.Forms.ComboBox();
            this.label1 = new System.Windows.Forms.Label();
            this.fullscreenBox = new System.Windows.Forms.CheckBox();
            this.contentPackageBox = new System.Windows.Forms.ComboBox();
            this.label2 = new System.Windows.Forms.Label();
            this.packageManagerButton = new System.Windows.Forms.Button();
            this.label3 = new System.Windows.Forms.Label();
            this.autoUpdateCheckBox = new System.Windows.Forms.CheckBox();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.patchNoteBox = new System.Windows.Forms.TextBox();
            this.updateLabel = new System.Windows.Forms.Label();
            this.downloadButton = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.SuspendLayout();
            // 
            // pictureBox1
            // 
            this.pictureBox1.BackColor = System.Drawing.Color.OrangeRed;
            this.pictureBox1.Image = ((System.Drawing.Image)(resources.GetObject("pictureBox1.Image")));
            this.pictureBox1.Location = new System.Drawing.Point(-11, 33);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(650, 42);
            this.pictureBox1.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pictureBox1.TabIndex = 0;
            this.pictureBox1.TabStop = false;
            // 
            // launchButton
            // 
            this.launchButton.BackColor = System.Drawing.Color.OrangeRed;
            this.launchButton.FlatAppearance.BorderColor = System.Drawing.Color.OrangeRed;
            this.launchButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.launchButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 14.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.launchButton.ForeColor = System.Drawing.SystemColors.ControlText;
            this.launchButton.Location = new System.Drawing.Point(455, 399);
            this.launchButton.Name = "launchButton";
            this.launchButton.Size = new System.Drawing.Size(161, 42);
            this.launchButton.TabIndex = 1;
            this.launchButton.Text = "LAUNCH";
            this.launchButton.UseVisualStyleBackColor = false;
            this.launchButton.Click += new System.EventHandler(this.launchButton_Click);
            // 
            // resolutionBox
            // 
            this.resolutionBox.AllowDrop = true;
            this.resolutionBox.FormattingEnabled = true;
            this.resolutionBox.Location = new System.Drawing.Point(369, 236);
            this.resolutionBox.Name = "resolutionBox";
            this.resolutionBox.Size = new System.Drawing.Size(212, 21);
            this.resolutionBox.TabIndex = 2;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.BackColor = System.Drawing.Color.Transparent;
            this.label1.ForeColor = System.Drawing.SystemColors.Window;
            this.label1.Location = new System.Drawing.Point(366, 220);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(60, 13);
            this.label1.TabIndex = 3;
            this.label1.Text = "Resolution:";
            // 
            // fullscreenBox
            // 
            this.fullscreenBox.AutoSize = true;
            this.fullscreenBox.BackColor = System.Drawing.Color.Transparent;
            this.fullscreenBox.ForeColor = System.Drawing.SystemColors.Window;
            this.fullscreenBox.Location = new System.Drawing.Point(369, 281);
            this.fullscreenBox.Name = "fullscreenBox";
            this.fullscreenBox.Size = new System.Drawing.Size(74, 17);
            this.fullscreenBox.TabIndex = 4;
            this.fullscreenBox.Text = "Fullscreen";
            this.fullscreenBox.UseVisualStyleBackColor = false;
            // 
            // contentPackageBox
            // 
            this.contentPackageBox.FormattingEnabled = true;
            this.contentPackageBox.Location = new System.Drawing.Point(369, 138);
            this.contentPackageBox.Name = "contentPackageBox";
            this.contentPackageBox.Size = new System.Drawing.Size(212, 21);
            this.contentPackageBox.TabIndex = 5;
            this.contentPackageBox.SelectedIndexChanged += new System.EventHandler(this.contentPackageBox_SelectedIndexChanged);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.BackColor = System.Drawing.Color.Transparent;
            this.label2.ForeColor = System.Drawing.SystemColors.Window;
            this.label2.Location = new System.Drawing.Point(366, 122);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(92, 13);
            this.label2.TabIndex = 6;
            this.label2.Text = "Content package:";
            // 
            // packageManagerButton
            // 
            this.packageManagerButton.BackColor = System.Drawing.Color.OrangeRed;
            this.packageManagerButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.packageManagerButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.packageManagerButton.Location = new System.Drawing.Point(369, 165);
            this.packageManagerButton.Name = "packageManagerButton";
            this.packageManagerButton.Size = new System.Drawing.Size(120, 35);
            this.packageManagerButton.TabIndex = 7;
            this.packageManagerButton.Text = "Package manager";
            this.packageManagerButton.UseVisualStyleBackColor = false;
            this.packageManagerButton.Click += new System.EventHandler(this.packageManagerButton_Click);
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.BackColor = System.Drawing.Color.Transparent;
            this.label3.ForeColor = System.Drawing.SystemColors.ControlLightLight;
            this.label3.Location = new System.Drawing.Point(266, 78);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(86, 13);
            this.label3.TabIndex = 8;
            this.label3.Text = "Installed version:";
            // 
            // autoUpdateCheckBox
            // 
            this.autoUpdateCheckBox.AutoSize = true;
            this.autoUpdateCheckBox.BackColor = System.Drawing.Color.Transparent;
            this.autoUpdateCheckBox.ForeColor = System.Drawing.SystemColors.Window;
            this.autoUpdateCheckBox.Location = new System.Drawing.Point(47, 122);
            this.autoUpdateCheckBox.Name = "autoUpdateCheckBox";
            this.autoUpdateCheckBox.Size = new System.Drawing.Size(177, 17);
            this.autoUpdateCheckBox.TabIndex = 9;
            this.autoUpdateCheckBox.Text = "Automatically check for updates";
            this.autoUpdateCheckBox.UseVisualStyleBackColor = false;
            // 
            // progressBar
            // 
            this.progressBar.Location = new System.Drawing.Point(129, 407);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(207, 30);
            this.progressBar.TabIndex = 10;
            // 
            // patchNoteBox
            // 
            this.patchNoteBox.AcceptsReturn = true;
            this.patchNoteBox.Location = new System.Drawing.Point(47, 145);
            this.patchNoteBox.Multiline = true;
            this.patchNoteBox.Name = "patchNoteBox";
            this.patchNoteBox.ReadOnly = true;
            this.patchNoteBox.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.patchNoteBox.Size = new System.Drawing.Size(289, 226);
            this.patchNoteBox.TabIndex = 11;
            // 
            // updateLabel
            // 
            this.updateLabel.AutoSize = true;
            this.updateLabel.BackColor = System.Drawing.Color.Transparent;
            this.updateLabel.ForeColor = System.Drawing.SystemColors.ButtonHighlight;
            this.updateLabel.Location = new System.Drawing.Point(44, 383);
            this.updateLabel.Name = "updateLabel";
            this.updateLabel.Size = new System.Drawing.Size(98, 13);
            this.updateLabel.TabIndex = 12;
            this.updateLabel.Text = "New update found!";
            // 
            // downloadButton
            // 
            this.downloadButton.BackColor = System.Drawing.Color.OrangeRed;
            this.downloadButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.downloadButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.downloadButton.Location = new System.Drawing.Point(47, 407);
            this.downloadButton.Name = "downloadButton";
            this.downloadButton.Size = new System.Drawing.Size(76, 30);
            this.downloadButton.TabIndex = 13;
            this.downloadButton.Text = "Download";
            this.downloadButton.UseVisualStyleBackColor = false;
            this.downloadButton.Click += new System.EventHandler(this.downloadButton_Click);
            // 
            // LauncherMain
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.SystemColors.ButtonHighlight;
            this.BackgroundImage = ((System.Drawing.Image)(resources.GetObject("$this.BackgroundImage")));
            this.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.ClientSize = new System.Drawing.Size(628, 453);
            this.Controls.Add(this.downloadButton);
            this.Controls.Add(this.updateLabel);
            this.Controls.Add(this.patchNoteBox);
            this.Controls.Add(this.progressBar);
            this.Controls.Add(this.autoUpdateCheckBox);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.packageManagerButton);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.contentPackageBox);
            this.Controls.Add(this.fullscreenBox);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.launchButton);
            this.Controls.Add(this.resolutionBox);
            this.Controls.Add(this.pictureBox1);
            this.DoubleBuffered = true;
            this.Name = "LauncherMain";
            this.Text = "Form1";
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.PictureBox pictureBox1;
        private System.Windows.Forms.Button launchButton;
        private System.Windows.Forms.ComboBox resolutionBox;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.CheckBox fullscreenBox;
        private System.Windows.Forms.ComboBox contentPackageBox;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Button packageManagerButton;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.CheckBox autoUpdateCheckBox;
        private System.Windows.Forms.ProgressBar progressBar;
        private System.Windows.Forms.TextBox patchNoteBox;
        private System.Windows.Forms.Label updateLabel;
        private System.Windows.Forms.Button downloadButton;
    }
}

