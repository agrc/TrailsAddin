namespace TrailsAddin
{
    internal class NumRouteCharsEditBox : ArcGIS.Desktop.Framework.Contracts.EditBox
    {
        public NumRouteCharsEditBox()
        {
            Main.Current.OnRouteNameChanged += OnRouteNameChange;
        }

        private void OnRouteNameChange(object sender, OnRouteNameChangedArgs args)
        {
            var length = args.name.Length;

            Text = length.ToString();

            if (length > 50)
            {
                Text = Text + '!';
            }
        }
    }
}
