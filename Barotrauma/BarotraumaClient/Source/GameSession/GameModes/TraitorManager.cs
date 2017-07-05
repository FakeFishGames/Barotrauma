namespace Barotrauma
{
    partial class TraitorManager
    {
        public static void CreateStartPopUp(string targetName)
        {
            new GUIMessageBox("You are the Traitor!",
                "Your secret task is to assassinate " + targetName + "! Discretion is an utmost concern; sinking the submarine and killing the entire crew "
                + "will arouse suspicion amongst the Fleet. If possible, make the death look like an accident.", 400, 350);
        }
    }
}
