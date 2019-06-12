
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

        public OpenFileDialog()
        {
            ofd = new System.Windows.Forms.OpenFileDialog();
        }

        public System.Windows.Forms.DialogResult ShowDialog()
        {
            ofd.Multiselect = Multiselect;
            ofd.InitialDirectory = InitialDirectory;
            ofd.Filter = Filter;
            ofd.Title = Title;

#if LINUX
            var wrapperForm = new WrapperForm(ofd);
            System.Windows.Forms.Application.Run(wrapperForm);
            System.Windows.Forms.Application.Exit();
            FileName = wrapperForm.FileName;
            FileNames = wrapperForm.FileNames;
            return wrapperForm.Result;
#else
            var result = ofd.ShowDialog();
            FileName = ofd.FileName;
            FileNames = ofd.FileNames;
            return result;
#endif
        }

#if LINUX
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
                this.Close();
            }
        }
#endif
    }
}
