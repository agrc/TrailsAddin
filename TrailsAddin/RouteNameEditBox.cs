namespace TrailsAddin
{
    internal class RouteNameEditBox : ArcGIS.Desktop.Framework.Contracts.EditBox
    {
        protected override void OnEnter()
        {
            Main.Current.AddNewRoute(Text);

            Text = "";
        }

        protected override void OnTextChange(string text)
        {
            Main.Current.ChangeRouteName(text);
        }
    }
}
