namespace TrailsAddin
{
    internal class RouteNameEditBox : ArcGIS.Desktop.Framework.Contracts.EditBox
    {
        protected override void OnEnter()
        {
            Main.AddNewRoute(Text);

            Text = "";
        }
    }
}
