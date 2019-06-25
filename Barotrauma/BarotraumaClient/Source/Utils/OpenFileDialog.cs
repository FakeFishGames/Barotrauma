
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    public class OpenFileDialog
    {
        private System.Windows.Forms.OpenFileDialog ofd;

        public bool Multiselect;
        public string InitialDirectory;
        public string Filter;
        public string Title;
        public string FileName { get; private set; }
        public string[] FileNames { get; private set; }

        public OpenFileDialog() { }

        public System.Windows.Forms.DialogResult ShowDialog()
        {
            ofd = new System.Windows.Forms.OpenFileDialog();
            ofd.Multiselect = Multiselect;
            ofd.InitialDirectory = InitialDirectory;
            ofd.Filter = Filter;
            ofd.Title = Title;

            System.Windows.Forms.DialogResult result;
            var wrapperForm = new WrapperForm(ofd);
            System.Windows.Forms.Application.Run(wrapperForm);
            FileName = wrapperForm.FileName;
            FileNames = wrapperForm.FileNames;
            result = wrapperForm.Result;
            ofd = null;
            return result;
        }

        private class WrapperForm : System.Windows.Forms.Form
        {
            private System.Windows.Forms.OpenFileDialog ofd;

            public System.Windows.Forms.DialogResult Result { get; private set; }
            public string FileName { get; private set; }
            public string[] FileNames { get; private set; }

            public WrapperForm(System.Windows.Forms.OpenFileDialog dialog)
            {
                ofd = dialog;
                Load += WrapperForm_Load;
            }

            private void WrapperForm_Load(object sender, EventArgs e)
            {
                Result = ofd.ShowDialog();
                FileName = ofd.FileName;
                FileNames = ofd.FileNames;
                System.Threading.Thread.Sleep(100);
                System.Windows.Forms.Application.Exit();
            }
        }
    }
}
