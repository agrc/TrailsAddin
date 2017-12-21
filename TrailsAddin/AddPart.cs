using ArcGIS.Desktop.Framework.Contracts;

namespace TrailsAddin
{
    internal class AddPart : Button
    {
        protected override void OnClick()
        {
            Main.Current.AddPart();
        }

        protected override void OnUpdate()
        {
            Enabled = Main.Current.BuildOnSelect;
        }
    }
}
