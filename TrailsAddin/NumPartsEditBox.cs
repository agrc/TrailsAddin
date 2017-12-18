namespace TrailsAddin
{
    internal class NumPartsEditBox : ArcGIS.Desktop.Framework.Contracts.EditBox
    {
        delegate void onPartNumberChange(int n);

        public NumPartsEditBox()
        {
            Main.Current.OnNumPartsChanged += OnPartNumberChange;
            Text = "1";
        }

        private void OnPartNumberChange(object sender, OnNumPartsChangedArgs args)
        {
            Text = args.numParts.ToString();
        }
    }
}
