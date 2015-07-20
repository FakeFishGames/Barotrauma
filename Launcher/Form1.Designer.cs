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
            this.comboBox2 = new System.Windows.Forms.ComboBox();
            this.label2 = new System.Windows.Forms.Label();
            this.button2 = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.SuspendLayout();
            // 
            // pictureBox1
            // 
            this.pictureBox1.Image = ((System.Drawing.Image)(resources.GetObject("pictureBox1.Image")));
            this.pictureBox1.Location = new System.Drawing.Point(57, 25);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(508, 69);
            this.pictureBox1.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.pictureBox1.TabIndex = 0;
            this.pictureBox1.TabStop = false;
            // 
            // launchButton
            // 
            this.launchButton.Location = new System.Drawing.Point(455, 399);
            this.launchButton.Name = "launchButton";
            this.launchButton.Size = new System.Drawing.Size(161, 42);
            this.launchButton.TabIndex = 1;
            this.launchButton.Text = "LAUNCH";
            this.launchButton.UseVisualStyleBackColor = true;
            this.launchButton.Click += new System.EventHandler(this.launchButton_Click);
            // 
            // resolutionBox
            // 
            this.resolutionBox.AllowDrop = true;
            this.resolutionBox.FormattingEnabled = true;
            this.resolutionBox.Location = new System.Drawing.Point(57, 207);
            this.resolutionBox.Name = "resolutionBox";
            this.resolutionBox.Size = new System.Drawing.Size(212, 21);
            this.resolutionBox.TabIndex = 2;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(54, 191);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(60, 13);
            this.label1.TabIndex = 3;
            this.label1.Text = "Resolution:";
            // 
            // fullscreenBox
            // 
            this.fullscreenBox.AutoSize = true;
            this.fullscreenBox.Location = new System.Drawing.Point(57, 252);
            this.fullscreenBox.Name = "fullscreenBox";
            this.fullscreenBox.Size = new System.Drawing.Size(74, 17);
            this.fullscreenBox.TabIndex = 4;
            this.fullscreenBox.Text = "Fullscreen";
            this.fullscreenBox.UseVisualStyleBackColor = true;
            // 
            // comboBox2
            // 
            this.comboBox2.FormattingEnabled = true;
            this.comboBox2.Location = new System.Drawing.Point(368, 207);
            this.comboBox2.Name = "comboBox2";
            this.comboBox2.Size = new System.Drawing.Size(212, 21);
            this.comboBox2.TabIndex = 5;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(365, 191);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(92, 13);
            this.label2.TabIndex = 6;
            this.label2.Text = "Content package:";
            // 
            // button2
            // 
            this.button2.Location = new System.Drawing.Point(368, 234);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(130, 35);
            this.button2.TabIndex = 7;
            this.button2.Text = "Package manager";
            this.button2.UseVisualStyleBackColor = true;
            // 
            // LauncherMain
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(628, 453);
            this.Controls.Add(this.button2);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.comboBox2);
            this.Controls.Add(this.fullscreenBox);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.resolutionBox);
            this.Controls.Add(this.launchButton);
            this.Controls.Add(this.pictureBox1);
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
        private System.Windows.Forms.ComboBox comboBox2;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Button button2;
    }
}

