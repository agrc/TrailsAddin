using ArcGIS.Desktop.Editing;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Dialogs;
using ArcGIS.Desktop.Framework.Threading.Tasks;

namespace TrailsAddin
{
    internal class AddIDs : Button
    {
        protected override void OnClick()
        {
            QueuedTask.Run(() =>
            {
                var operation = new EditOperation();
                operation.Name = "Add IDs to selected segments and trailheads";

                Main.Current.EnsureIDsForSelected(operation);

                var success = operation.Execute();
                if (!success)
                {
                    MessageBox.Show(operation.ErrorMessage);
                } else
                {
                    Main.Current.HeadsLayer.ClearSelection();
                    Main.Current.SegmentsLayer.ClearSelection();
                }
            });
        }

        protected override void OnUpdate()
        {
            var main = Main.Current;
            Enabled = (main.SegmentsLayer.SelectionCount > 0 || main.HeadsLayer.SelectionCount > 0);
        }
    }
}
