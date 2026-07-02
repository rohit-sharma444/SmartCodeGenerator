using Microsoft.Office.Interop.Outlook;
using System;

namespace EmailAnswering
{
    public partial class ThisAddIn
    {
        private void ThisAddIn_Startup(object sender, EventArgs e)
        {
            EmailExplorer.InitializeExplorer();
            EmailExplorer.explorer = this.Application.ActiveExplorer();
            EmailExplorer.explorer.SelectionChange += Explorer_SelectionChange;
            EmailExplorer.selection = EmailExplorer.explorer.Selection;
        }

        private void Explorer_SelectionChange()
        {
            Selection sel = EmailExplorer.explorer.Selection;

            if (sel.Count > 0 && sel[1] is MailItem mailItem)
                EmailExplorer.selection = sel;
        }

        private void ThisAddIn_Shutdown(object sender, System.EventArgs e)
        {
            if (EmailExplorer.explorer != null)
                EmailExplorer.explorer.SelectionChange -= Explorer_SelectionChange;
        }

        #region VSTO generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InternalStartup()
        {
            this.Startup += new System.EventHandler(ThisAddIn_Startup);
            this.Shutdown += new System.EventHandler(ThisAddIn_Shutdown);
        }

        #endregion
    }
}
