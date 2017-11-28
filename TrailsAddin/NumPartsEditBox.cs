namespace TrailsAddin
{
    internal class NumPartsEditBox : ArcGIS.Desktop.Framework.Contracts.EditBox
    {
        public NumPartsEditBox()
        {
            Main.RaiseOnAddPart += OnAddPart;
        }

        void OnAddPart(object sender, OnAddPartArgs args)
        {
            Text = args.NumParts.ToString();
        }
    }
}
