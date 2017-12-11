using ArcGIS.Desktop.Framework.Threading.Tasks;

namespace TrailsAddin
{
    internal class ToggleSelectCheckbox : ArcGIS.Desktop.Framework.Contracts.CheckBox
    {
        public ToggleSelectCheckbox()
        {
            IsChecked = false;
            Enabled = true;
        }

        protected override void OnClick()
        {
            Main.Current.BuildOnSelect = (bool)IsChecked;

            QueuedTask.Run(() =>
            {
                Main.Current.SegmentsLayer.ClearSelection();
                Main.Current.OnCancelButtonClick();
            }));
        }
    }
}
